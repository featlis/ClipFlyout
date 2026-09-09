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
    private readonly object _lock = new();

    public event Action? HotkeyPressed;

    public bool IsRegistered => _isRegistered;

    public void Start()
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(Start);
            return;
        }

        lock (_lock)
        {
            if (_isRegistered) return;

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
            if (!_isRegistered || _hwndSource == null) return;

            Win32.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
            _isRegistered = false;
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
