using System;
using System.Windows.Interop;
using ClipFlyout.Native;
using WpfApplication = System.Windows.Application;

namespace ClipFlyout.Services;

public class HotkeyService : IDisposable
{
    private const int HotkeyId = 9001;
    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private bool _registrationFailed;
    private readonly object _lock = new();

    public event Action? HotkeyPressed;

    public bool IsRegistered => _isRegistered;
    public bool RegistrationFailed => _registrationFailed;

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
            if (_isRegistered) return;

            CleanupHwndLocked();

            var parameters = new HwndSourceParameters("ClipFlyoutHotkeyListener")
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

            // Alt + Shift + C
            uint modifiers = Win32.MOD_ALT | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT;
            if (Win32.RegisterHotKey(_hwndSource.Handle, HotkeyId, modifiers, (uint)'C'))
            {
                _isRegistered = true;
                _registrationFailed = false;
            }
            else
            {
                // Clean up HWND immediately on failure so repeated Start() calls never leak HWNDs
                CleanupHwndLocked();
                _isRegistered = false;
                _registrationFailed = true;
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
            if (_isRegistered && _hwndSource != null)
            {
                Win32.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            }
            CleanupHwndLocked();
            _isRegistered = false;
            _registrationFailed = false;
        }
    }

    private void CleanupHwndLocked()
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }
}
