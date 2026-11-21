using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;
using Microsoft.Win32;

namespace ClipFlyout.Views;

/// <summary>
/// Lightweight, event-driven Windows 11 taskbar widget pill.
/// Eliminates legacy 30ms polling and synchronous GPU-stalling GetPixel loops.
/// </summary>
public partial class TaskbarWidgetWindow : Window
{
    private DetectionResult? _currentResult;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly DispatcherTimer _guardTimer;

    private bool _isHovered;
    private bool _isPressed;
    private bool _isHiddenByGuard;
    private bool _cachedTaskbarIsLight;
    private IntPtr _hwnd = IntPtr.Zero;
    private uint _taskbarCreatedMsg;

    private Win32.RECT _lastTaskbarRect;
    private Win32.RECT _lastTrayRect;
    private IntPtr _lastTargetMonitor;

    // Frozen Fluent brushes (zero per-tick allocations)
    private static readonly SolidColorBrush HitTestTransparent = ThemeService.CreateFrozenBrush(Color.FromArgb(1, 0, 0, 0));
    private static readonly SolidColorBrush DarkTextBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(24, 24, 24));
    private static readonly SolidColorBrush LightTextBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(245, 245, 245));
    private static readonly SolidColorBrush LightBarHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(18, 0, 0, 0));
    private static readonly SolidColorBrush LightBarPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(30, 0, 0, 0));
    private static readonly SolidColorBrush LightBarHoverBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(24, 0, 0, 0));
    private static readonly SolidColorBrush DarkBarHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(24, 255, 255, 255));
    private static readonly SolidColorBrush DarkBarPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(14, 255, 255, 255));
    private static readonly SolidColorBrush DarkBarHoverBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(28, 255, 255, 255));
    private static readonly SolidColorBrush AboveLightBg = ThemeService.CreateFrozenBrush(Color.FromArgb(232, 249, 249, 249));
    private static readonly SolidColorBrush AboveDarkBg = ThemeService.CreateFrozenBrush(Color.FromArgb(232, 36, 36, 36));
    private static readonly SolidColorBrush AboveLightBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(32, 0, 0, 0));
    private static readonly SolidColorBrush AboveDarkBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(34, 255, 255, 255));

    public IntPtr Handle => _hwnd;

    public static bool IsFullScreenApplicationActive(IntPtr excludeHwnd = default) =>
        Win32.IsFullscreenAppActive(excludeHwnd);

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? RecallRequested;
    public event Action? SettingsRequested;

    private readonly Action _themeChangedHandler;
    private readonly Action _languageChangedHandler;
    private readonly Action<AppSettings> _settingsChangedHandler;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();

        // Gentle 500ms guard (16x lighter than legacy 30ms polling) — stopped completely when hidden!
        _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _guardTimer.Tick += GuardTimer_Tick;

        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshTaskbarLuminanceCache();
                    ApplyTheme();
                });
        };
        _languageChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(ApplyLocalization);
        };
        _settingsChangedHandler = cfg =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => OnSettingsChanged(cfg));
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && IsVisible) UpdatePosition(force: true);
        };
        Closed += OnClosed;

        ThemeService.Instance.ThemeChanged += _themeChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        _settings.SettingsChanged += _settingsChangedHandler;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        RefreshTaskbarLuminanceCache();
        ApplyLocalization();
        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);

        _taskbarCreatedMsg = Win32.RegisterWindowMessage("TaskbarCreated");
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_SETTINGCHANGE)
        {
            Dispatcher.BeginInvoke(() =>
            {
                RefreshTaskbarLuminanceCache();
                ApplyTheme();
                UpdatePosition(force: true);
            });
        }
        else if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
        {
            Dispatcher.BeginInvoke(() => UpdatePosition(force: true));
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshTaskbarLuminanceCache();
        ApplyTheme();
        UpdatePosition(force: true);
        SyncTimerState();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _guardTimer.Stop();
        ThemeService.Instance.ThemeChanged -= _themeChangedHandler;
        _loc.LanguageChanged -= _languageChangedHandler;
        _settings.SettingsChanged -= _settingsChangedHandler;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(() => UpdatePosition(force: true));
        }
    }

    private void OnSettingsChanged(AppSettings cfg)
    {
        if (cfg.ShowTaskbarWidget)
        {
            if (!_isHiddenByGuard && !IsVisible) Show();
            RefreshTaskbarLuminanceCache();
            ApplyTheme();
            UpdatePosition(force: true);
            SyncTimerState();
        }
        else
        {
            _guardTimer.Stop();
            if (IsVisible) Hide();
        }
        UpdateContextMenuChecks(cfg);
    }

    private void SyncTimerState()
    {
        if (_settings.Current.ShowTaskbarWidget)
        {
            if (!_guardTimer.IsEnabled) _guardTimer.Start();
        }
        else
        {
            _guardTimer.Stop();
        }
    }

    private void GuardTimer_Tick(object? sender, EventArgs e)
    {
        var cfg = _settings.Current;
        if (!cfg.ShowTaskbarWidget)
        {
            _guardTimer.Stop();
            if (IsVisible) Hide();
            return;
        }

        // Hide cleanly during fullscreen apps OR when Start Menu / Quick Settings / Shell Overflow is active
        // so the widget never floats on top of Start Menu or games!
        bool shouldHide = Win32.IsFullscreenAppActive(_hwnd) ||
                          (cfg.WidgetPosition != WidgetPositionMode.AboveTaskbar && Win32.IsShellOverlayActive());

        if (shouldHide)
        {
            if (!_isHiddenByGuard)
            {
                _isHiddenByGuard = true;
                if (IsVisible) Hide();
            }
            return;
        }

        if (_isHiddenByGuard)
        {
            _isHiddenByGuard = false;
            if (!IsVisible) Show();
            UpdatePosition(force: true);
            return;
        }

        // Only reposition if taskbar/tray geometry or active cursor monitor actually changed
        UpdatePosition(force: false);

        // Keep gently above taskbar only when visible and not competing with shell overlays
        if (_hwnd != IntPtr.Zero && IsVisible && WidgetContextMenu?.IsOpen != true)
        {
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
    }

    private void RefreshTaskbarLuminanceCache()
    {
        _cachedTaskbarIsLight = TaskbarColorDetector.IsTaskbarLightFast();
    }

    private bool ResolveLightTextSurface()
    {
        var cfg = _settings.Current;
        return cfg.WidgetTextColor switch
        {
            WidgetTextColorMode.Light => false, // White text (for dark taskbar)
            WidgetTextColorMode.Dark => true,   // Dark text (for light taskbar)
            _ => _cachedTaskbarIsLight
        };
    }

    public void ApplyTheme()
    {
        var cfg = _settings.Current;
        bool isLightSurface = ResolveLightTextSurface();
        bool isAbove = cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        if (isAbove)
        {
            bool isDarkApp = ThemeService.Instance.IsDarkTheme;
            RootPill.Background = isDarkApp ? AboveDarkBg : AboveLightBg;
            RootPill.BorderBrush = isDarkApp ? AboveDarkBorder : AboveLightBorder;
            var fg = isDarkApp ? LightTextBrush : DarkTextBrush;
            ClipPreviewText.Foreground = fg;
            FluentTypeIcon.Foreground = fg;
        }
        else
        {
            var fg = isLightSurface ? DarkTextBrush : LightTextBrush;
            ClipPreviewText.Foreground = fg;
            FluentTypeIcon.Foreground = fg;

            if (_isPressed)
            {
                RootPill.Background = isLightSurface ? LightBarPressedBg : DarkBarPressedBg;
                RootPill.BorderBrush = isLightSurface ? LightBarHoverBorder : DarkBarHoverBorder;
            }
            else if (_isHovered)
            {
                RootPill.Background = isLightSurface ? LightBarHoverBg : DarkBarHoverBg;
                RootPill.BorderBrush = isLightSurface ? LightBarHoverBorder : DarkBarHoverBorder;
            }
            else
            {
                RootPill.Background = HitTestTransparent;
                RootPill.BorderBrush = Brushes.Transparent;
            }
        }
    }

    public void ApplyLocalization()
    {
        if (_currentResult == null)
        {
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
            RootPill.ToolTip = _loc.Get("Widget_ClickHint");
        }

        MenuOpenFlyout.Header = _loc.Get("Tray_Settings_Flyout");
        MenuPosition.Header = _loc.Get("Widget_Position_Header");
        MenuPosTrayLeft.Header = _loc.Get("Widget_Pos_TrayLeft");
        MenuPosCenterRight.Header = _loc.Get("Widget_Pos_CenterRight");
        MenuPosCenterLeft.Header = _loc.Get("Widget_Pos_CenterLeft");
        MenuPosFarLeft.Header = _loc.Get("Widget_Pos_FarLeft");
        MenuPosAboveTaskbar.Header = _loc.Get("Widget_Pos_AboveTaskbar");

        MenuAutoAlign.Header = _loc.Get("Setting_WidgetAutoAlign");
        MenuMonitor.Header = _loc.Get("Widget_Monitor_Header");
        MenuMonPrimary.Header = _loc.Get("Widget_Monitor_Primary");
        MenuMonCursor.Header = _loc.Get("Widget_Monitor_Cursor");
        MenuMonDisplay1.Header = _loc.Get("Widget_Monitor_Display1");
        MenuMonDisplay2.Header = _loc.Get("Widget_Monitor_Display2");
        MenuMonDisplay3.Header = _loc.Get("Widget_Monitor_Display3");

        MenuTextColor.Header = _loc.Get("Setting_WidgetTextColor");
        MenuTextColorAuto.Header = _loc.Get("Widget_TextColor_Auto");
        MenuTextColorLight.Header = _loc.Get("Widget_TextColor_Light");
        MenuTextColorDark.Header = _loc.Get("Widget_TextColor_Dark");

        MenuOpenSettings.Header = _loc.Get("Tray_Settings");
        MenuHideWidget.Header = _loc.Get("Widget_Hide");

        UpdateContextMenuChecks(_settings.Current);
    }

    private void UpdateContextMenuChecks(AppSettings cfg)
    {
        MenuPosTrayLeft.IsChecked = cfg.WidgetPosition == WidgetPositionMode.TrayLeft;
        MenuPosCenterRight.IsChecked = cfg.WidgetPosition == WidgetPositionMode.CenterRight;
        MenuPosCenterLeft.IsChecked = cfg.WidgetPosition == WidgetPositionMode.CenterLeft;
        MenuPosFarLeft.IsChecked = cfg.WidgetPosition == WidgetPositionMode.FarLeft;
        MenuPosAboveTaskbar.IsChecked = cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        MenuAutoAlign.IsChecked = cfg.WidgetAutoAlign;

        MenuMonPrimary.IsChecked = cfg.WidgetMonitor == WidgetMonitorTarget.Primary;
        MenuMonCursor.IsChecked = cfg.WidgetMonitor == WidgetMonitorTarget.FollowCursor;
        MenuMonDisplay1.IsChecked = cfg.WidgetMonitor == WidgetMonitorTarget.Monitor1;
        MenuMonDisplay2.IsChecked = cfg.WidgetMonitor == WidgetMonitorTarget.Monitor2;
        MenuMonDisplay3.IsChecked = cfg.WidgetMonitor == WidgetMonitorTarget.Monitor3;

        MenuTextColorAuto.IsChecked = cfg.WidgetTextColor == WidgetTextColorMode.Auto;
        MenuTextColorLight.IsChecked = cfg.WidgetTextColor == WidgetTextColorMode.Light;
        MenuTextColorDark.IsChecked = cfg.WidgetTextColor == WidgetTextColorMode.Dark;
    }

    public void UpdateClipContent(DetectionResult result)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateClipContent(result));
            return;
        }

        _currentResult = result;

        string snippet = HistoryService.BuildCleanSnippet(result);
        if (string.IsNullOrWhiteSpace(snippet)) snippet = result.PreviewTitle;

        if (result.Type == ClipDataType.HexColor && result.ColorValue.HasValue)
        {
            FluentTypeIcon.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Visible;
            ColorBox.Background = ThemeService.CreateFrozenBrush(result.ColorValue.Value);
            ClipPreviewText.Text = result.HexColorCode ?? snippet;
        }
        else
        {
            ColorBox.Visibility = Visibility.Collapsed;
            FluentTypeIcon.Visibility = Visibility.Visible;
            FluentTypeIcon.Text = FlyoutWindow.GetTypeGlyph(result.Type);
            ClipPreviewText.Text = snippet;
        }

        // Rich full-text tooltip with click hint
        string fullPreview = result.Type == ClipDataType.Image
            ? $"{result.PreviewTitle} ({result.PreviewSubtitle})"
            : $"{result.PreviewTitle}\n{snippet}\n{_loc.Get("Widget_ClickHint")}";
        RootPill.ToolTip = fullPreview;

        UpdateLayout();
        UpdatePosition(force: true);
    }

    private IntPtr ResolveTargetMonitor(WidgetMonitorTarget target)
    {
        if (target == WidgetMonitorTarget.FollowCursor && Win32.GetCursorPos(out var pt))
        {
            return Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        }

        if (target is WidgetMonitorTarget.Monitor1 or WidgetMonitorTarget.Monitor2 or WidgetMonitorTarget.Monitor3)
        {
            var monitors = Win32.GetAllMonitors();
            int idx = target switch
            {
                WidgetMonitorTarget.Monitor1 => 0,
                WidgetMonitorTarget.Monitor2 => 1,
                _ => 2
            };
            if (idx < monitors.Count) return monitors[idx];
        }

        return Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
    }

    private IntPtr FindTaskbarForMonitor(IntPtr hMonitor)
    {
        IntPtr primaryTb = Win32.FindWindow("Shell_TrayWnd", null);
        if (primaryTb != IntPtr.Zero && Win32.MonitorFromWindow(primaryTb, Win32.MONITOR_DEFAULTTONEAREST) == hMonitor)
        {
            return primaryTb;
        }

        IntPtr secTb = IntPtr.Zero;
        while ((secTb = Win32.FindWindowEx(IntPtr.Zero, secTb, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (Win32.MonitorFromWindow(secTb, Win32.MONITOR_DEFAULTTONEAREST) == hMonitor)
            {
                return secTb;
            }
        }

        return primaryTb;
    }

    private static bool RectsEqual(Win32.RECT a, Win32.RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private void UpdatePosition(bool force)
    {
        var cfg = _settings.Current;
        IntPtr hMonitor = ResolveTargetMonitor(cfg.WidgetMonitor);
        IntPtr taskbarHwnd = FindTaskbarForMonitor(hMonitor);

        Win32.RECT tbRect = default;
        bool hasTaskbar = taskbarHwnd != IntPtr.Zero && Win32.GetWindowRect(taskbarHwnd, out tbRect);

        Win32.RECT trayRect = default;
        bool hasTray = false;
        if (hasTaskbar && cfg.WidgetAutoAlign)
        {
            IntPtr trayHwnd = Win32.FindWindowEx(taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayHwnd != IntPtr.Zero && Win32.GetWindowRect(trayHwnd, out trayRect))
            {
                hasTray = true;
            }
        }

        if (!force &&
            hMonitor == _lastTargetMonitor &&
            RectsEqual(tbRect, _lastTaskbarRect) &&
            RectsEqual(trayRect, _lastTrayRect))
        {
            return;
        }

        _lastTargetMonitor = hMonitor;
        _lastTaskbarRect = tbRect;
        _lastTrayRect = trayRect;

        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(hMonitor, ref mi))
        {
            // Clean DIP fallback (no double DPI division!)
            var wa = SystemParameters.WorkArea;
            double w = ActualWidth > 0 ? ActualWidth : MinWidth;
            Left = Math.Max(wa.Left + 8, wa.Right - w - 16 + cfg.WidgetOffsetX);
            Top = wa.Bottom - Height - 8;
            return;
        }

        // Per-Monitor V2 accurate DPI scale using target hMonitor
        double dpi = Win32.GetMonitorDpiScale(hMonitor, IntPtr.Zero);
        double monLeft = mi.rcMonitor.Left / dpi;
        double monRight = mi.rcMonitor.Right / dpi;
        double monBottom = mi.rcMonitor.Bottom / dpi;
        double workBottom = mi.rcWork.Bottom / dpi;

        double tbLeft = hasTaskbar ? tbRect.Left / dpi : monLeft;
        double tbTop = hasTaskbar ? tbRect.Top / dpi : workBottom;
        double tbRight = hasTaskbar ? tbRect.Right / dpi : monRight;
        double tbBottom = hasTaskbar ? tbRect.Bottom / dpi : monBottom;
        double tbHeight = Math.Max(40, tbBottom - tbTop);

        double widgetWidth = ActualWidth > 0 ? ActualWidth : MinWidth;
        double targetLeft;
        double targetTop;

        if (cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar)
        {
            targetLeft = monRight - widgetWidth - 16;
            targetTop = Math.Min(workBottom, tbTop) - Height - 10;
        }
        else
        {
            targetTop = tbTop + (tbHeight - Height) / 2.0;

            switch (cfg.WidgetPosition)
            {
                case WidgetPositionMode.FarLeft:
                    targetLeft = tbLeft + 12;
                    break;
                case WidgetPositionMode.CenterLeft:
                    targetLeft = tbLeft + (tbRight - tbLeft) * 0.28 - widgetWidth / 2.0;
                    break;
                case WidgetPositionMode.CenterRight:
                    targetLeft = tbLeft + (tbRight - tbLeft) * 0.72 - widgetWidth / 2.0;
                    break;
                case WidgetPositionMode.TrayLeft:
                default:
                    if (hasTray)
                    {
                        targetLeft = (trayRect.Left / dpi) - widgetWidth - 8;
                    }
                    else
                    {
                        // Leave ~140px margin on secondary taskbars so we don't overlap the clock
                        targetLeft = tbRight - widgetWidth - 144;
                    }
                    break;
            }
        }

        targetLeft += cfg.WidgetOffsetX;
        Left = Math.Clamp(targetLeft, monLeft + 4, Math.Max(monLeft + 4, monRight - widgetWidth - 4));
        Top = targetTop;
    }

    private void RootPill_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        ApplyTheme();
    }

    private void RootPill_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        ApplyTheme();
    }

    private void RootPill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPressed = true;
        ApplyTheme();
    }

    private void RootPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPressed = false;
        ApplyTheme();

        if (_currentResult != null)
        {
            FlyoutRequested?.Invoke(_currentResult);
        }
        else
        {
            RecallRequested?.Invoke();
        }
    }

    private void MenuOpenFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (_currentResult != null) FlyoutRequested?.Invoke(_currentResult);
        else RecallRequested?.Invoke();
    }

    private void MenuPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<WidgetPositionMode>(tag, out var mode))
        {
            _settings.UpdateSettings(s => s.WidgetPosition = mode);
        }
    }

    private void MenuAutoAlign_Click(object sender, RoutedEventArgs e)
    {
        _settings.UpdateSettings(s => s.WidgetAutoAlign = MenuAutoAlign.IsChecked);
    }

    private void MenuMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<WidgetMonitorTarget>(tag, out var target))
        {
            _settings.UpdateSettings(s => s.WidgetMonitor = target);
        }
    }

    private void MenuTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<WidgetTextColorMode>(tag, out var mode))
        {
            _settings.UpdateSettings(s => s.WidgetTextColor = mode);
        }
    }

    private void MenuOpenSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void MenuHideWidget_Click(object sender, RoutedEventArgs e)
    {
        _settings.UpdateSettings(s => s.ShowTaskbarWidget = false);
    }
}
