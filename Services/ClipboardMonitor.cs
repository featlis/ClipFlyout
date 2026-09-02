using System;
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
    private bool _isEnabled = true;
    private readonly object _lock = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isListening) return;

            // Ensure HwndSource is created on UI thread
            if (!WpfApplication.Current.Dispatcher.CheckAccess())
            {
                WpfApplication.Current.Dispatcher.Invoke(Start);
                return;
            }

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
        }
    }

    public void Stop()
    {
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
        // Delay slightly before decrementing to absorb immediate clipboard event
        Task.Delay(300).ContinueWith(_ =>
        {
            Interlocked.Decrement(ref _suppressCount);
        });
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
            if (!_isEnabled || Volatile.Read(ref _suppressCount) > 0)
            {
                return IntPtr.Zero;
            }

            // Read clipboard safely with retry
            ReadClipboardAsync();
        }

        return IntPtr.Zero;
    }

    private void ReadClipboardAsync()
    {
        Task.Run(() =>
        {
            // Try reading clipboard with 5 retries (80ms interval) to handle lock contention
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    object? extractedData = null;

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        if (WpfClipboard.ContainsImage())
                        {
                            var img = WpfClipboard.GetImage();
                            if (img != null)
                            {
                                // Freeze for thread safety
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
                    });

                    if (extractedData != null)
                    {
                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            ClipboardChanged?.Invoke(this, extractedData);
                        });
                        break;
                    }
                }
                catch
                {
                    // Clipboard was locked by another process, wait and retry
                    Thread.Sleep(80);
                }
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }
}
