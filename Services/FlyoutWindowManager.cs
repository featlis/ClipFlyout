using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Views;

namespace ClipFlyout.Services;

public class FlyoutWindowManager : IDisposable
{
    private readonly FlyoutWindow _window;
    private readonly DispatcherTimer _autoHideTimer;
    private bool _isShowing;

    public FlyoutWindowManager(ActionExecutor executor)
    {
        _window = new FlyoutWindow();
        _window.MouseEntered += OnMouseEntered;
        _window.MouseLeft += OnMouseLeft;
        _window.CloseRequested += OnCloseRequested;

        executor.ActionExecuted += OnActionExecuted;

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(3500)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
    }

    public void ShowFlyout(DetectionResult result)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => ShowFlyout(result));
            return;
        }

        _autoHideTimer.Stop();

        // Position window on current active monitor (where mouse cursor is)
        UpdateWindowPosition();

        _window.Present(result);
        _isShowing = true;

        // Start auto hide timer (3.5s)
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(3500);
        _autoHideTimer.Start();
    }

    public void HideFlyout()
    {
        if (!_isShowing) return;

        _autoHideTimer.Stop();
        _window.AnimateHide(() =>
        {
            _isShowing = false;
        });
    }

    private void UpdateWindowPosition()
    {
        // Measure window size
        _window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetWidth = _window.Width;
        double targetHeight = _window.DesiredSize.Height > 0 ? _window.DesiredSize.Height : 200;

        // Get cursor position
        Win32.GetCursorPos(out var cursorPos);

        // Find monitor from cursor
        IntPtr hMonitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();

        if (Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            // DPI scaling calculation
            var source = PresentationSource.FromVisual(_window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double workLeft = monitorInfo.rcWork.Left / dpiX;
            double workTop = monitorInfo.rcWork.Top / dpiY;
            double workRight = monitorInfo.rcWork.Right / dpiX;
            double workBottom = monitorInfo.rcWork.Bottom / dpiY;

            // Place in bottom-right corner of the active monitor's work area
            _window.Left = workRight - targetWidth - 24;
            _window.Top = workBottom - targetHeight - 24;
        }
        else
        {
            // Fallback to primary screen
            _window.Left = SystemParameters.WorkArea.Right - targetWidth - 24;
            _window.Top = SystemParameters.WorkArea.Bottom - targetHeight - 24;
        }
    }

    private void OnMouseEntered()
    {
        // Pause timer on hover
        _autoHideTimer.Stop();
    }

    private void OnMouseLeft()
    {
        // Resume countdown with 1.5s
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _autoHideTimer.Start();
    }

    private void OnCloseRequested()
    {
        HideFlyout();
    }

    private void OnActionExecuted(string message)
    {
        _autoHideTimer.Stop();
        _window.ShowToastFeedback(message);
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        HideFlyout();
    }

    public void Dispose()
    {
        _autoHideTimer.Stop();
        _window.Close();
    }
}
