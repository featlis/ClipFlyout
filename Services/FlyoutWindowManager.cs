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

        // Position window based on user placement setting & active monitor
        UpdateWindowPosition();

        _window.Present(result);
        _isShowing = true;

        // Start auto-hide timer using user preference
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
        _window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetWidth = _window.Width;
        double targetHeight = _window.DesiredSize.Height > 0 ? _window.DesiredSize.Height : 180;

        // Get cursor position
        Win32.GetCursorPos(out var cursorPos);

        // Find monitor from cursor
        IntPtr hMonitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();

        double workLeft, workTop, workRight, workBottom;

        if (Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            var source = PresentationSource.FromVisual(_window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            workLeft = monitorInfo.rcWork.Left / dpiX;
            workTop = monitorInfo.rcWork.Top / dpiY;
            workRight = monitorInfo.rcWork.Right / dpiX;
            workBottom = monitorInfo.rcWork.Bottom / dpiY;

            double cursorX = cursorPos.X / dpiX;
            double cursorY = cursorPos.Y / dpiY;

            switch (_settings.Current.Placement)
            {
                case FlyoutPlacement.TopRight:
                    _window.Left = workRight - targetWidth - 24;
                    _window.Top = workTop + 24;
                    break;

                case FlyoutPlacement.TopLeft:
                    _window.Left = workLeft + 24;
                    _window.Top = workTop + 24;
                    break;

                case FlyoutPlacement.BottomLeft:
                    _window.Left = workLeft + 24;
                    _window.Top = workBottom - targetHeight - 24;
                    break;

                case FlyoutPlacement.NearCursor:
                    // Offset by +16px from cursor, keep inside work area
                    double posX = cursorX + 16;
                    double posY = cursorY + 16;

                    if (posX + targetWidth > workRight) posX = cursorX - targetWidth - 16;
                    if (posY + targetHeight > workBottom) posY = cursorY - targetHeight - 16;

                    _window.Left = Math.Max(workLeft + 10, posX);
                    _window.Top = Math.Max(workTop + 10, posY);
                    break;

                case FlyoutPlacement.BottomRight:
                default:
                    _window.Left = workRight - targetWidth - 24;
                    _window.Top = workBottom - targetHeight - 24;
                    break;
            }
        }
        else
        {
            // Primary screen fallback
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
