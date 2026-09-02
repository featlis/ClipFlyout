using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
    private readonly SettingsService _settings = SettingsService.Instance;
    private bool _isShowing;

    public FlyoutWindow Window => _window;

    public FlyoutWindowManager(ActionExecutor executor)
    {
        _window = new FlyoutWindow();
        _window.MouseEntered += OnMouseEntered;
        _window.MouseLeft += OnMouseLeft;
        _window.CloseRequested += OnCloseRequested;

        executor.ActionExecuted += OnActionExecuted;

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.Current.DisplayDurationSeconds)
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

        // 1. Populate content first so actual size is measurable
        _window.Present(result);
        _window.Measure(new Size(352, double.PositiveInfinity));

        // 2. Position window based on actual measured size & active monitor
        UpdateWindowPosition();

        _isShowing = true;

        // 3. Start auto-hide timer using user preference
        double durationSec = Math.Max(1.0, _settings.Current.DisplayDurationSeconds);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(durationSec);
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
        double targetWidth = _window.Width > 0 ? _window.Width : 352;
        double targetHeight = _window.DesiredSize.Height > 0 ? _window.DesiredSize.Height : 160;

        // Get cursor position
        Win32.GetCursorPos(out var cursorPos);

        // Find monitor from cursor
        IntPtr hMonitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();

        if (Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            double dpiScale = Win32.GetMonitorDpiScale(hMonitor, hwnd);

            double workLeft = monitorInfo.rcWork.Left / dpiScale;
            double workTop = monitorInfo.rcWork.Top / dpiScale;
            double workRight = monitorInfo.rcWork.Right / dpiScale;
            double workBottom = monitorInfo.rcWork.Bottom / dpiScale;

            double cursorX = cursorPos.X / dpiScale;
            double cursorY = cursorPos.Y / dpiScale;

            double targetLeft;
            double targetTop;

            switch (_settings.Current.Placement)
            {
                case FlyoutPlacement.TopRight:
                    targetLeft = workRight - targetWidth - 20;
                    targetTop = workTop + 20;
                    break;

                case FlyoutPlacement.TopLeft:
                    targetLeft = workLeft + 20;
                    targetTop = workTop + 20;
                    break;

                case FlyoutPlacement.BottomLeft:
                    targetLeft = workLeft + 20;
                    targetTop = workBottom - targetHeight - 20;
                    break;

                case FlyoutPlacement.NearCursor:
                    targetLeft = cursorX + 16;
                    targetTop = cursorY + 16;

                    if (targetLeft + targetWidth > workRight) targetLeft = cursorX - targetWidth - 16;
                    if (targetTop + targetHeight > workBottom) targetTop = cursorY - targetHeight - 16;
                    break;

                case FlyoutPlacement.BottomRight:
                default:
                    targetLeft = workRight - targetWidth - 20;
                    targetTop = workBottom - targetHeight - 20;
                    break;
            }

            // Strict screen boundary clamping: Never allow window to overflow off-screen
            double minLeft = workLeft + 12;
            double maxLeft = Math.Max(minLeft, workRight - targetWidth - 12);
            double minTop = workTop + 12;
            double maxTop = Math.Max(minTop, workBottom - targetHeight - 12);

            _window.Left = Math.Clamp(targetLeft, minLeft, maxLeft);
            _window.Top = Math.Clamp(targetTop, minTop, maxTop);
        }
        else
        {
            // Primary screen fallback
            _window.Left = Math.Max(12, SystemParameters.WorkArea.Right - targetWidth - 20);
            _window.Top = Math.Max(12, SystemParameters.WorkArea.Bottom - targetHeight - 20);
        }
    }

    private void OnMouseEntered()
    {
        // Pause timer on hover
        _autoHideTimer.Stop();
    }

    private void OnMouseLeft()
    {
        // Resume countdown with user configured hover leave time
        double leaveSec = Math.Max(0.5, _settings.Current.HoverLeaveDurationSeconds);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(leaveSec);
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
