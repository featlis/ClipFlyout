using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ClipFlyout.Native;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipFlyout.Services;

public class ClipboardMonitor : IClipboardMonitor
{
    public event EventHandler<object>? ClipboardChanged;

    private HwndSource? _hwndSource;
    private bool _isListening;
    private int _suppressCount;
    private int _suppressedEventsDuringWrite;
    private long _clipboardReadGeneration;
    private volatile bool _isEnabled = true;
    private readonly object _lock = new();
    private readonly HashSet<uint> _suppressedSequenceNumbers = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public void Start()
    {
        // HwndSource is affinity-bound to the WPF UI thread. Dispatch before
        // acquiring the lock, otherwise a background caller can deadlock with
        // the UI thread while it tries to re-enter Start().
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(Start);
            return;
        }

        lock (_lock)
        {
            if (_isListening) return;

            var parameters = new HwndSourceParameters("ClipFlyoutClipboardListener")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                ParentWindow = IntPtr.Zero
            };

            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(HwndHook);

            if (Win32.AddClipboardFormatListener(_hwndSource.Handle))
            {
                _isListening = true;
            }
            else
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource.Dispose();
                _hwndSource = null;
            }
        }
    }

    public void Stop()
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(Stop);
            return;
        }

        lock (_lock)
        {
            if (!_isListening || _hwndSource == null) return;

            Win32.RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
            _isListening = false;
        }
    }

    public IDisposable SuppressNotifications()
    {
        Interlocked.Increment(ref _suppressCount);
        return new SuppressionScope(this);
    }

    private void ReleaseSuppression()
    {
        if (Interlocked.Decrement(ref _suppressCount) != 0)
        {
            return;
        }

        // Use Windows' clipboard sequence number instead of a timing window.
        // That suppresses only the exact update written by our action, while a
        // user copy immediately afterwards is still processed.
        if (Interlocked.Exchange(ref _suppressedEventsDuringWrite, 0) == 0)
        {
            uint sequenceNumber = Win32.GetClipboardSequenceNumber();
            lock (_suppressedSequenceNumbers)
            {
                // A missed native notification (for example while monitoring
                // is toggled off) must not allow this small bookkeeping cache
                // to grow without bound.
                if (_suppressedSequenceNumbers.Count >= 32)
                {
                    _suppressedSequenceNumbers.Clear();
                }
                _suppressedSequenceNumbers.Add(sequenceNumber);
            }
        }
    }

    private sealed class SuppressionScope : IDisposable
    {
        private readonly ClipboardMonitor _monitor;
        private int _disposed;

        public SuppressionScope(ClipboardMonitor monitor)
        {
            _monitor = monitor;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _monitor.ReleaseSuppression();
            }
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE)
        {
            if (!_isEnabled)
            {
                return IntPtr.Zero;
            }

            if (Volatile.Read(ref _suppressCount) > 0)
            {
                Interlocked.Increment(ref _suppressedEventsDuringWrite);
                return IntPtr.Zero;
            }

            uint sequenceNumber = Win32.GetClipboardSequenceNumber();
            lock (_suppressedSequenceNumbers)
            {
                if (_suppressedSequenceNumbers.Remove(sequenceNumber))
                {
                    return IntPtr.Zero;
                }
            }

            // Each update gets a generation. Retries for an older clipboard
            // value are discarded when a newer copy arrives.
            long generation = Interlocked.Increment(ref _clipboardReadGeneration);
            _ = ReadClipboardAsync(generation);
        }

        return IntPtr.Zero;
    }

    private async Task ReadClipboardAsync(long generation)
    {
        // The WPF Clipboard API must run on the UI dispatcher. Keeping retry
        // waits asynchronous avoids blocking a worker thread or the UI.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (generation != Volatile.Read(ref _clipboardReadGeneration) || !_isEnabled)
            {
                return;
            }

            try
            {
                object? extractedData = null;

                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (WpfClipboard.ContainsImage())
                    {
                        var img = WpfClipboard.GetImage();
                        if (img != null)
                        {
                            // Freeze before passing the image to analysis on a
                            // worker thread.
                            if (img.CanFreeze) img.Freeze();
                            extractedData = img;
                        }
                    }
                    else if (WpfClipboard.ContainsText())
                    {
                        string text = WpfClipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            extractedData = text;
                        }
                    }
                    if (extractedData != null && generation == Volatile.Read(ref _clipboardReadGeneration) && _isEnabled)
                    {
                        ClipboardChanged?.Invoke(this, extractedData);
                    }
                });

                return;
            }
            catch
            {
                // Clipboard was locked by another process. A newer update ends
                // the loop early on its next iteration.
                await Task.Delay(80).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
