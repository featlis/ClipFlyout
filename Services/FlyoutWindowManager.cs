using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Views;

namespace ClipFlyout.Services;

public class FlyoutWindowManager : IDisposable
{
    private readonly FlyoutWindow _window;
    private readonly ActionExecutor _executor;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly SettingsService _settings = SettingsService.Instance;
    private bool _isShowing;
    private bool _isButtonHovered;
    private bool _keyboardActiveMode;
    private DetectionResult? _lastResult;
    private IntPtr _lastExternalForegroundHwnd;
    private (double Left, double Top, double Right, double Bottom) _currentWorkArea;
    private (int Left, int Top, int Right, int Bottom) _currentPhysWorkArea;
    private double _currentTargetDpi = 1.0;

    // Lightweight hooks active ONLY while flyout is visible (0 overhead when hidden)
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Win32.LowLevelHookProc? _mouseHookProc;
    private Win32.LowLevelHookProc? _keyboardHookProc;
    private Func<IntPtr>? _taskbarWidgetHwndProvider;
    private Func<DetectionResult?>? _currentClipboardFallbackProvider;

    public FlyoutWindow Window => _window;
    public DetectionResult? LastResult => _lastResult;
    public IntPtr LastExternalForegroundHwnd => _lastExternalForegroundHwnd;
    public bool IsShowing => _isShowing;

    public event Action? SettingsRequested;

    public FlyoutWindowManager(ActionExecutor executor)
    {
        _executor = executor;
        _window = new FlyoutWindow();
        _window.MouseEntered += OnMouseEntered;
        _window.MouseLeft += OnMouseLeft;
        _window.ButtonMouseEntered += OnButtonMouseEntered;
        _window.ButtonMouseLeft += OnButtonMouseLeft;
        _window.CloseRequested += OnCloseRequested;
        _window.SettingsRequested += () =>
        {
            HideFlyout();
            SettingsRequested?.Invoke();
        };
        _window.HistoryItemSelected += result => ShowFlyout(result, recordHistory: false, interactive: true);
        _window.QuickTextRequested += OnQuickTextRequested;
        _window.QuickPasteResultRequested += OnQuickPasteResultRequested;
        _window.InteractiveInputModeChanged += active =>
        {
            if (active) _autoHideTimer?.Stop();
            else if (_isShowing) StartHoverLeaveTimer();
        };
        _window.SizeChanged += OnWindowSizeChanged;

        executor.ActionExecuted += OnActionExecuted;
        executor.TransformedActionExecuted += OnTransformedActionExecuted;
        executor.ActionFailed += OnActionFailed;
        executor.BeforeModalDialog += HideFlyout;

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.Current.DisplayDurationSeconds)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
    }

    public void SetTaskbarWidgetHwndProvider(Func<IntPtr> provider) => _taskbarWidgetHwndProvider = provider;
    public void SetCurrentClipboardFallbackProvider(Func<DetectionResult?> provider) => _currentClipboardFallbackProvider = provider;

    public void CaptureExternalForegroundWindow()
    {
        IntPtr fg = Win32.GetForegroundWindow();
        if (Win32.IsValidExternalTargetWindow(fg))
        {
            _lastExternalForegroundHwnd = fg;
        }
    }

    public void ClearLastResultIfUnpinned()
    {
        if (_lastResult != null && !HistoryService.Instance.IsPinned(_lastResult))
        {
            _lastResult = null;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isShowing) return;

        if (_currentPhysWorkArea.Right > _currentPhysWorkArea.Left && e.NewSize.Height > 0)
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero && Win32.GetWindowRect(hwnd, out var curRect))
            {
                var placement = _settings.Current.Placement;
                int physW = Math.Max(1, curRect.Right - curRect.Left);
                int physH = (int)Math.Round(e.NewSize.Height * _currentTargetDpi);
                int newPhysTop = curRect.Top;

                if (placement == FlyoutPlacement.BottomRight || placement == FlyoutPlacement.BottomLeft)
                {
                    if (e.PreviousSize.Height > 0)
                    {
                        int deltaPhysH = (int)Math.Round((e.NewSize.Height - e.PreviousSize.Height) * _currentTargetDpi);
                        newPhysTop = curRect.Top - deltaPhysH;
                    }
                    else
                    {
                        newPhysTop = _currentPhysWorkArea.Bottom - physH - (int)Math.Round(16 * _currentTargetDpi);
                    }
                }

                int margin = (int)Math.Round(12 * _currentTargetDpi);
                int minTop = _currentPhysWorkArea.Top + margin;
                int maxTop = Math.Max(minTop, _currentPhysWorkArea.Bottom - physH - margin);
                newPhysTop = Math.Clamp(newPhysTop, minTop, maxTop);

                Win32.SetWindowPos(hwnd, IntPtr.Zero, curRect.Left, newPhysTop, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return;
            }
        }

        if (_currentWorkArea.Bottom > _currentWorkArea.Top && e.NewSize.Height > 0)
        {
            var placement = _settings.Current.Placement;
            if (placement == FlyoutPlacement.BottomRight || placement == FlyoutPlacement.BottomLeft)
            {
                if (e.PreviousSize.Height > 0)
                {
                    double deltaH = e.NewSize.Height - e.PreviousSize.Height;
                    _window.Top -= deltaH;
                }
                else
                {
                    _window.Top = _currentWorkArea.Bottom - e.NewSize.Height - 16;
                }
            }

            double minTop = _currentWorkArea.Top + 12;
            double maxTop = Math.Max(minTop, _currentWorkArea.Bottom - e.NewSize.Height - 12);
            _window.Top = Math.Clamp(_window.Top, minTop, maxTop);
        }
    }

    public void ShowFlyout(DetectionResult result, bool recordHistory = true, bool interactive = false)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => ShowFlyout(result, recordHistory, interactive));
            return;
        }

        CaptureExternalForegroundWindow();

        // Suppress automatic flyouts during fullscreen games, videos, or presentations
        if (!interactive && _settings.Current.SuppressInFullscreen)
        {
            IntPtr ownHwnd = new WindowInteropHelper(_window).Handle;
            if (Win32.IsFullscreenAppActive(ownHwnd))
            {
                _lastResult = result;
                if (recordHistory) HistoryService.Instance.Add(result);
                return;
            }
        }

        _lastResult = result;
        if (recordHistory)
        {
            HistoryService.Instance.Add(result);
        }

        _autoHideTimer.Stop();
        _isButtonHovered = false;
        _keyboardActiveMode = interactive;

        // 1. Populate content and measure full desired size
        _window.Present(result, canPin: recordHistory || HistoryService.Instance.GetTimestamp(result).HasValue);
        double measuredHeight = _window.MeasureContentHeight();

        // 2. Position window based on target monitor physical pixels & measured size
        UpdateWindowPosition(measuredHeight);

        // 3. Mark showing BEFORE ShowFlyout so SizeChanged stays consistent
        _isShowing = true;
        _window.ShowFlyout();
        // Re-apply physical position right after Show() to guarantee cross-DPI placement
        UpdateWindowPosition(measuredHeight);
        InstallDismissHooks();

        // 4. Start auto-hide countdown (longer grace period if opened interactively)
        double durationSec = Math.Max(1.5, _settings.Current.DisplayDurationSeconds);
        if (interactive) durationSec = Math.Max(durationSec, 6.0);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(durationSec);
        _autoHideTimer.Start();
    }

    public void HideFlyout()
    {
        if (!_isShowing) return;

        _autoHideTimer.Stop();
        _isButtonHovered = false;
        _keyboardActiveMode = false;
        UninstallDismissHooks();

        _window.AnimateHide(() =>
        {
            _isShowing = false;
        });
    }

    public void ToggleOrRecallFlyout()
    {
        if (_isShowing)
        {
            HideFlyout();
            return;
        }

        RecallLastFlyout();
    }

    public void RecallLastFlyout()
    {
        if (_lastResult != null)
        {
            ShowFlyout(_lastResult, recordHistory: false, interactive: true);
            return;
        }

        var fallback = _currentClipboardFallbackProvider?.Invoke();
        if (fallback != null)
        {
            ShowFlyout(fallback, recordHistory: true, interactive: true);
        }
    }

    private void InstallDismissHooks()
    {
        try
        {
            IntPtr hMod = Win32.GetModuleHandle(null);
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseHookProc = MouseHookCallback;
                _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
            }
            if (_keyboardHook == IntPtr.Zero)
            {
                _keyboardHookProc = KeyboardHookCallback;
                _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
            }
        }
        catch
        {
        }
    }

    private void UninstallDismissHooks()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseHookProc = null;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _keyboardHookProc = null;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isShowing)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                if (!IsPointInsideWindow(new WindowInteropHelper(_window).Handle, info.pt) &&
                    !IsPointInsideWidget(info.pt))
                {
                    _window.Dispatcher.BeginInvoke(HideFlyout);
                }
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isShowing)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                uint vk = info.vkCode;

                // Escape dismisses open flyout, but only eats keystroke when user interacted with flyout
                // so passive auto-flyouts never swallow Esc in Vim/VSCode/Excel.
                if (vk == Win32.VK_ESCAPE)
                {
                    bool eat = _keyboardActiveMode || IsCursorOverFlyout() || _window.IsTextInputActive;
                    _window.Dispatcher.BeginInvoke(HideFlyout);
                    if (eat) return (IntPtr)1;
                    return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                }

                // Navigation & quick-fire keys apply ONLY when hovering flyout or opened interactively
                // and NEVER while typing inside search/edit boxes!
                bool canCaptureNav = (_keyboardActiveMode || IsCursorOverFlyout()) && !_window.IsTextInputActive;
                if (canCaptureNav)
                {
                    if (vk == Win32.VK_DOWN)
                    {
                        _window.Dispatcher.BeginInvoke(() => _window.MoveSelection(1));
                        return (IntPtr)1;
                    }
                    if (vk == Win32.VK_UP)
                    {
                        _window.Dispatcher.BeginInvoke(() => _window.MoveSelection(-1));
                        return (IntPtr)1;
                    }
                    if (vk == Win32.VK_RETURN)
                    {
                        _window.Dispatcher.BeginInvoke(() => _window.ExecuteSelectedAction());
                        return (IntPtr)1;
                    }
                    // Number keys '1'..'9' (0x31..0x39) without Ctrl/Alt/Shift/Win modifiers
                    bool anyModifierHeld = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0 ||
                                           (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0 ||
                                           (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0 ||
                                           (Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0 ||
                                           (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0;
                    if (!anyModifierHeld && vk >= 0x31 && vk <= 0x39)
                    {
                        int idx = (int)(vk - 0x31);
                        _window.Dispatcher.BeginInvoke(() => _window.ExecuteActionByIndex(idx));
                        return (IntPtr)1;
                    }
                }
            }
        }
        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool IsPointInsideWidget(Win32.POINT pt)
    {
        if (_taskbarWidgetHwndProvider == null) return false;
        IntPtr widgetHwnd = _taskbarWidgetHwndProvider();
        return widgetHwnd != IntPtr.Zero && IsPointInsideWindow(widgetHwnd, pt);
    }

    private static bool IsPointInsideWindow(IntPtr hwnd, Win32.POINT pt)
    {
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return false;
        return pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
    }

    private void UpdateWindowPosition(double? explicitHeight = null)
    {
        double targetWidth = _window.ActualWidth > 0 ? _window.ActualWidth : (_window.Width > 0 ? _window.Width : 356);
        double targetHeight = explicitHeight ?? (_window.ActualHeight > 0 ? _window.ActualHeight : _window.MeasureContentHeight());
        if (targetHeight <= 0) targetHeight = 320;

        Win32.GetCursorPos(out var cursorPos);
        IntPtr hMonitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };

        if (Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            double dpiScale = Win32.GetMonitorDpiScale(hMonitor, hwnd);
            _currentTargetDpi = dpiScale;

            int physWorkLeft = monitorInfo.rcWork.Left;
            int physWorkTop = monitorInfo.rcWork.Top;
            int physWorkRight = monitorInfo.rcWork.Right;
            int physWorkBottom = monitorInfo.rcWork.Bottom;
            _currentPhysWorkArea = (physWorkLeft, physWorkTop, physWorkRight, physWorkBottom);

            double workLeft = physWorkLeft / dpiScale;
            double workTop = physWorkTop / dpiScale;
            double workRight = physWorkRight / dpiScale;
            double workBottom = physWorkBottom / dpiScale;
            _currentWorkArea = (workLeft, workTop, workRight, workBottom);

            double maxAllowedHeight = Math.Max(180, workBottom - workTop - 24);
            if (_window.MaxHeight != maxAllowedHeight)
            {
                _window.MaxHeight = maxAllowedHeight;
            }
            targetHeight = Math.Min(targetHeight, maxAllowedHeight);

            int physW = (int)Math.Round(targetWidth * dpiScale);
            int physH = (int)Math.Round(targetHeight * dpiScale);
            int pad16 = (int)Math.Round(16 * dpiScale);
            int pad12 = (int)Math.Round(12 * dpiScale);

            int targetPhysX;
            int targetPhysY;

            switch (_settings.Current.Placement)
            {
                case FlyoutPlacement.TopRight:
                    targetPhysX = physWorkRight - physW - pad16;
                    targetPhysY = physWorkTop + pad16;
                    break;

                case FlyoutPlacement.TopLeft:
                    targetPhysX = physWorkLeft + pad16;
                    targetPhysY = physWorkTop + pad16;
                    break;

                case FlyoutPlacement.BottomLeft:
                    targetPhysX = physWorkLeft + pad16;
                    targetPhysY = physWorkBottom - physH - pad16;
                    break;

                case FlyoutPlacement.NearCursor:
                    targetPhysX = cursorPos.X + pad16;
                    targetPhysY = cursorPos.Y + pad16;
                    if (targetPhysX + physW > physWorkRight - pad12) targetPhysX = cursorPos.X - physW - pad16;
                    if (targetPhysY + physH > physWorkBottom - pad12) targetPhysY = cursorPos.Y - physH - pad16;
                    break;

                case FlyoutPlacement.BottomRight:
                default:
                    targetPhysX = physWorkRight - physW - pad16;
                    targetPhysY = physWorkBottom - physH - pad16;
                    break;
            }

            int minPhysX = physWorkLeft + pad12;
            int maxPhysX = Math.Max(minPhysX, physWorkRight - physW - pad12);
            int minPhysY = physWorkTop + pad12;
            int maxPhysY = Math.Max(minPhysY, physWorkBottom - physH - pad12);

            targetPhysX = Math.Clamp(targetPhysX, minPhysX, maxPhysX);
            targetPhysY = Math.Clamp(targetPhysY, minPhysY, maxPhysY);

            if (hwnd != IntPtr.Zero)
            {
                // Position using exact physical virtual-desktop pixels so mixed-DPI monitors never overshoot
                Win32.SetWindowPos(hwnd, IntPtr.Zero, targetPhysX, targetPhysY, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
            else
            {
                _window.Left = targetPhysX / dpiScale;
                _window.Top = targetPhysY / dpiScale;
            }
        }
        else
        {
            double workLeft = SystemParameters.WorkArea.Left;
            double workTop = SystemParameters.WorkArea.Top;
            double workRight = SystemParameters.WorkArea.Right;
            double workBottom = SystemParameters.WorkArea.Bottom;
            _currentWorkArea = (workLeft, workTop, workRight, workBottom);

            _window.Left = Math.Max(12, workRight - targetWidth - 16);
            _window.Top = Math.Max(12, workBottom - targetHeight - 16);
        }
    }

    private void OnButtonMouseEntered()
    {
        _isButtonHovered = true;
        _autoHideTimer.Stop();
    }

    private void OnButtonMouseLeft()
    {
        _isButtonHovered = false;
        if (_window.IsMouseOver || IsCursorOverFlyout())
        {
            _autoHideTimer.Stop();
            return;
        }
        StartHoverLeaveTimer();
    }

    private void OnMouseEntered()
    {
        _autoHideTimer.Stop();
    }

    private void OnMouseLeft()
    {
        if (_isButtonHovered || IsCursorOverFlyout())
        {
            _autoHideTimer.Stop();
            return;
        }
        StartHoverLeaveTimer();
    }

    private void StartHoverLeaveTimer()
    {
        double leaveSec = Math.Max(0.5, _settings.Current.HoverLeaveDurationSeconds);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(leaveSec);
        _autoHideTimer.Start();
    }

    private void OnCloseRequested() => HideFlyout();

    private void OnActionExecuted(string message)
    {
        _autoHideTimer.Stop();
        _window.ShowToastFeedback(message);
    }

    private void OnActionFailed(string message)
    {
        _autoHideTimer.Stop();
        _window.ShowToastFeedback(message, isError: true, autoClose: false);
        if (!_isButtonHovered && !_window.IsMouseOver && !IsCursorOverFlyout() && _isShowing)
        {
            StartHoverLeaveTimer();
        }
    }

    private void OnQuickTextRequested(string text, bool pasteImmediately)
    {
        _executor.CopyText(text, "Toast_Copied");
        if (pasteImmediately)
        {
            IntPtr target = _lastExternalForegroundHwnd;
            HideFlyout();
            _executor.PasteToActiveWindow(notifyToast: false, targetHwnd: target);
        }
    }

    private void OnQuickPasteResultRequested(DetectionResult result)
    {
        if (result.RawData is string text)
        {
            _executor.CopyText(text, "Toast_Copied");
        }
        else if (result.RawData is string[] paths && paths.Length > 0)
        {
            _executor.CopyText(string.Join(Environment.NewLine, paths), "Toast_Copied");
        }
        else if (result.ImagePreview != null)
        {
            _executor.CopyImage(result.ImagePreview, "Toast_Copied");
        }
        else
        {
            return;
        }

        IntPtr target = _lastExternalForegroundHwnd;
        HideFlyout();
        _executor.PasteToActiveWindow(notifyToast: false, targetHwnd: target);
    }

    private void OnTransformedActionExecuted(string transformedText, string label)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => OnTransformedActionExecuted(transformedText, label));
            return;
        }

        var loc = LocalizationService.Instance;
        var actions = new List<ActionItem>
        {
            new(
                "Action_Paste",
                loc.Get("Action_Paste"),
                "DocumentPaste24",
                loc.Get("Action_Paste_Desc"),
                () =>
                {
                    HideFlyout();
                    IntPtr targetHwnd = _lastExternalForegroundHwnd;
                    // Wait briefly so flyout dismissal finishes before restoring focus & sending Ctrl+V
                    _ = Task.Delay(140).ContinueWith(_ => _executor.PasteToActiveWindow(notifyToast: false, targetHwnd));
                },
                IsPrimary: true,
                Subtitle: "Ctrl+V"
            ),
            new(
                "Action_CopyTransformed",
                loc.Get("Action_Copy"),
                "Copy24",
                loc.Get("Action_Copy_Desc"),
                () => _executor.CopyText(transformedText, "Toast_Copied")
            )
        };

        string snippet = transformedText.Length > 180 ? transformedText[..180] + "…" : transformedText;

        var result = new DetectionResult(
            Type: ClipDataType.PlainText,
            RawData: transformedText,
            PreviewTitle: loc.Get("Transform_Title"),
            PreviewSubtitle: label,
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: loc.Get("Badge_Transformed")
        );

        // Don't clutter history with transient Transform confirmation card
        ShowFlyout(result, recordHistory: false, interactive: true);
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        if (_isButtonHovered || _window.IsMouseOver || IsCursorOverFlyout() || _window.IsTextInputActive)
        {
            // Keep checking periodically instead of stopping forever (prevents stuck-open flyout)
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(800);
            return;
        }

        HideFlyout();
    }

    private bool IsCursorOverFlyout()
    {
        if (!_window.IsVisible) return false;
        if (!Win32.GetCursorPos(out var cursorPos)) return false;

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle == IntPtr.Zero) return false;
        if (!Win32.GetWindowRect(helper.Handle, out var rect)) return false;

        return cursorPos.X >= rect.Left - 2 && cursorPos.X <= rect.Right + 2 &&
               cursorPos.Y >= rect.Top - 2 && cursorPos.Y <= rect.Bottom + 2;
    }

    public void Dispose()
    {
        UninstallDismissHooks();
        _autoHideTimer.Stop();
        _window.SizeChanged -= OnWindowSizeChanged;
        _window.Close();
    }
}
