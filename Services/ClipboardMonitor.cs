using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public event Action? PauseStateChanged;

    private HwndSource? _hwndSource;
    private bool _isListening;
    private int _suppressCount;
    private int _suppressedEventsDuringWrite;
    private long _suppressedUntilTicks;
    private long _clipboardReadGeneration;
    private volatile bool _isEnabled = true;
    private DateTimeOffset? _pauseUntilUtc;
    private System.Windows.Threading.DispatcherTimer? _pauseTimer;
    private readonly object _lock = new();
    private readonly HashSet<uint> _suppressedSequenceNumbers = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public bool IsPaused
    {
        get
        {
            if (_pauseUntilUtc is not DateTimeOffset until) return false;
            if (until > DateTimeOffset.UtcNow) return true;

            _pauseUntilUtc = null;
            return false;
        }
    }

    public DateTimeOffset? PauseUntilUtc => IsPaused ? _pauseUntilUtc : null;

    public void PauseFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            Resume();
            return;
        }

        _pauseUntilUtc = DateTimeOffset.UtcNow.Add(duration);

        var app = WpfApplication.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
        {
            void ArmTimer()
            {
                _pauseTimer?.Stop();
                _pauseTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, 86400))
                };
                _pauseTimer.Tick += (_, _) => Resume();
                _pauseTimer.Start();
            }

            if (app.Dispatcher.CheckAccess()) ArmTimer();
            else app.Dispatcher.BeginInvoke(ArmTimer);
        }

        PauseStateChanged?.Invoke();
    }

    public void Resume()
    {
        _pauseUntilUtc = null;
        var app = WpfApplication.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
        {
            void StopTimer() => _pauseTimer?.Stop();
            if (app.Dispatcher.CheckAccess()) StopTimer();
            else app.Dispatcher.BeginInvoke(StopTimer);
        }
        PauseStateChanged?.Invoke();
    }

    public void Start()
    {
        var app = WpfApplication.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Start);
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
        var app = WpfApplication.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            if (app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.Invoke(Stop);
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

        uint sequenceNumber = Win32.GetClipboardSequenceNumber();
        // Record both the current sequence number and a tiny 120ms guard for OleFlushClipboard double-bumps
        Volatile.Write(ref _suppressedUntilTicks, Environment.TickCount64 + 120);

        if (Interlocked.Exchange(ref _suppressedEventsDuringWrite, 0) == 0)
        {
            lock (_suppressedSequenceNumbers)
            {
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

    private static readonly HashSet<string> SensitiveProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "bitwarden", "keepass", "keepassxc", "lastpass", "dashlane", "enpass", "nordpass", "roboform", "protonpass"
    };

    internal static bool IsSensitiveProcessName(string processName)
    {
        return !string.IsNullOrWhiteSpace(processName) && SensitiveProcessNames.Contains(NormalizeProcessName(processName));
    }

    public static bool IsProcessExcluded(string? processName, bool ignorePasswordManagers, string? customExcludedApps)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        string cleanProc = NormalizeProcessName(processName);
        if (string.IsNullOrEmpty(cleanProc)) return false;

        if (ignorePasswordManagers && SensitiveProcessNames.Contains(cleanProc))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(customExcludedApps)) return false;

        string[] entries = customExcludedApps.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in entries)
        {
            string entry = NormalizeProcessName(raw);
            if (!string.IsNullOrEmpty(entry) && string.Equals(entry, cleanProc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeProcessName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].TrimEnd();
        }
        return trimmed;
    }

    private static bool IsExcludedWindow(IntPtr hwnd, Models.AppSettings cfg)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return IsProcessExcluded(proc.ProcessName, cfg.IgnorePasswordManagers, cfg.ExcludedApps);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates Windows Cloud Clipboard & password-manager ignore formats properly per Microsoft specification:
    /// - "Clipboard Viewer Ignore": presence alone means exclude
    /// - "CanIncludeInClipboardHistory": 4-byte DWORD where 0 = exclude, non-zero = allow
    /// - "ExcludeClipboardContentFromMonitorProcessing": presence with non-zero DWORD (or empty/non-zero) = exclude
    /// </summary>
    internal static bool ShouldExcludeClipboardByMetadata(
        bool hasViewerIgnore,
        bool hasExcludeMonitor,
        int? excludeMonitorDword,
        bool hasCanIncludeHistory,
        int? canIncludeHistoryDword)
    {
        if (hasViewerIgnore) return true;
        if (hasExcludeMonitor && (!excludeMonitorDword.HasValue || excludeMonitorDword.Value != 0)) return true;
        if (hasCanIncludeHistory && canIncludeHistoryDword.HasValue && canIncludeHistoryDword.Value == 0) return true;
        return false;
    }

    private static int? TryReadClipboardDword(string format)
    {
        try
        {
            object? data = WpfClipboard.GetData(format);
            if (data is MemoryStream ms && ms.Length >= 4)
            {
                ms.Position = 0;
                byte[] buf = new byte[4];
                if (ms.Read(buf, 0, 4) == 4)
                {
                    return BitConverter.ToInt32(buf, 0);
                }
            }
            else if (data is byte[] bytes && bytes.Length >= 4)
            {
                return BitConverter.ToInt32(bytes, 0);
            }
            else if (data is int intVal)
            {
                return intVal;
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool IsExcludedByClipboardFormats()
    {
        bool hasViewerIgnore = WpfClipboard.ContainsData("Clipboard Viewer Ignore");
        if (hasViewerIgnore) return true;

        bool hasExcludeMonitor = WpfClipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing");
        int? excludeMonitorDword = hasExcludeMonitor ? TryReadClipboardDword("ExcludeClipboardContentFromMonitorProcessing") : null;

        bool hasCanInclude = WpfClipboard.ContainsData("CanIncludeInClipboardHistory");
        int? canIncludeDword = hasCanInclude ? TryReadClipboardDword("CanIncludeInClipboardHistory") : null;

        return ShouldExcludeClipboardByMetadata(hasViewerIgnore, hasExcludeMonitor, excludeMonitorDword, hasCanInclude, canIncludeDword);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE)
        {
            bool wasPaused = _pauseUntilUtc.HasValue;
            if (IsPaused)
            {
                return IntPtr.Zero;
            }
            if (wasPaused)
            {
                PauseStateChanged?.Invoke();
            }

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

            // Catch immediate OleFlushClipboard secondary bump without blocking real user copies
            if (Environment.TickCount64 <= Volatile.Read(ref _suppressedUntilTicks))
            {
                IntPtr ownerNow = Win32.GetClipboardOwner();
                if (ownerNow != IntPtr.Zero)
                {
                    Win32.GetWindowThreadProcessId(ownerNow, out uint ownerPid);
                    if (ownerPid == (uint)Environment.ProcessId)
                    {
                        return IntPtr.Zero;
                    }
                }
            }

            IntPtr owner = Win32.GetClipboardOwner();
            if (owner == IntPtr.Zero) owner = Win32.GetForegroundWindow();

            long generation = Interlocked.Increment(ref _clipboardReadGeneration);
            _ = ReadClipboardAsync(generation, owner);
        }

        return IntPtr.Zero;
    }

    private async Task ReadClipboardAsync(long generation, IntPtr ownerHwnd)
    {
        var cfg = SettingsService.Instance.Current;
        if (IsPaused) return;

        // Check excluded/sensitive process off the synchronous WndProc hot path
        if (cfg.IgnorePasswordManagers || !string.IsNullOrWhiteSpace(cfg.ExcludedApps))
        {
            bool excluded = await Task.Run(() => IsExcludedWindow(ownerHwnd, cfg)).ConfigureAwait(false);
            if (excluded) return;
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (generation != Volatile.Read(ref _clipboardReadGeneration) || !_isEnabled || IsPaused)
            {
                return;
            }

            var app = WpfApplication.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted) return;

            try
            {
                object? extractedData = null;

                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (SettingsService.Instance.Current.IgnorePasswordManagers && IsExcludedByClipboardFormats())
                    {
                        return;
                    }

                    extractedData = ExtractClipboardPayloadOnUiThread();

                    if (extractedData != null && generation == Volatile.Read(ref _clipboardReadGeneration) && _isEnabled)
                    {
                        ClipboardChanged?.Invoke(this, extractedData);
                    }
                });

                return;
            }
            catch
            {
                await Task.Delay(80).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads the current clipboard payload once on the UI thread (used on startup to hydrate TaskbarWidget
    /// or when recall hotkey is pressed with empty history).
    /// </summary>
    public object? TryReadCurrentClipboard()
    {
        try
        {
            if (!_isEnabled || IsPaused) return null;

            var app = WpfApplication.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted) return null;

            var cfg = SettingsService.Instance.Current;
            bool ignorePm = cfg.IgnorePasswordManagers;
            if (ignorePm || !string.IsNullOrWhiteSpace(cfg.ExcludedApps))
            {
                IntPtr owner = Win32.GetClipboardOwner();
                if (owner != IntPtr.Zero && IsExcludedWindow(owner, cfg))
                {
                    return null;
                }
            }

            object? result = null;
            if (app.Dispatcher.CheckAccess())
            {
                if (!ignorePm || !IsExcludedByClipboardFormats())
                {
                    result = ExtractClipboardPayloadOnUiThread();
                }
            }
            else
            {
                app.Dispatcher.Invoke(() =>
                {
                    if (!ignorePm || !IsExcludedByClipboardFormats())
                    {
                        result = ExtractClipboardPayloadOnUiThread();
                    }
                });
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static object? ExtractClipboardPayloadOnUiThread()
    {
        // 1. Text payload FIRST (rich editors/browsers sometimes attach both text and virtual FileDrop;
        //    Windows Explorer file copies never set CF_UNICODETEXT, so Explorer FileDropList still triggers cleanly below)
        if (WpfClipboard.ContainsText())
        {
            string text = WpfClipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // 2. Explorer FileDropList (files/folders copied in File Explorer)
        if (WpfClipboard.ContainsFileDropList())
        {
            var files = WpfClipboard.GetFileDropList();
            if (files != null && files.Count > 0)
            {
                var paths = new string[files.Count];
                files.CopyTo(paths, 0);
                var valid = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                if (valid.Length > 0)
                {
                    return valid;
                }
            }
        }

        // 3. PNG stream (preserves alpha transparency better than WPF DIB GetImage)
        if (WpfClipboard.ContainsData("PNG"))
        {
            try
            {
                if (WpfClipboard.GetData("PNG") is MemoryStream pngStream && pngStream.Length > 0)
                {
                    pngStream.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = pngStream;
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
            }
        }

        // 4. Standard DIB/Bitmap image
        if (WpfClipboard.ContainsImage())
        {
            var img = WpfClipboard.GetImage();
            if (img != null)
            {
                if (img.CanFreeze) img.Freeze();
                return img;
            }
        }

        return null;
    }

    public void Dispose()
    {
        var app = WpfApplication.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
        {
            if (app.Dispatcher.CheckAccess()) _pauseTimer?.Stop();
            else app.Dispatcher.BeginInvoke(() => _pauseTimer?.Stop());
        }
        Stop();
    }
}
