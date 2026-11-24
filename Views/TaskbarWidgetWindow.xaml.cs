using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
/// Event-driven Windows 11 Fluent 2 taskbar widget pill.
/// Features frosted surface contrast safety, synchronous zero-jitter physical-pixel placement,
/// instant foreground WinEvent Z-order recovery, and multi-monitor/auto-hide awareness.
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
    private bool _isPaused;
    private bool _isApplyingPosition;
    private bool _cachedTaskbarIsLight;
    private IntPtr _hwnd = IntPtr.Zero;
    private uint _taskbarCreatedMsg;

    // Foreground WinEvent hook for 0ms Start Menu hide and instant Z-order recovery on taskbar click
    private IntPtr _foregroundHook = IntPtr.Zero;
    private Win32.WinEventDelegate? _winEventDelegate;

    private Win32.RECT _lastTaskbarRect;
    private Win32.RECT _lastTrayRect;
    private IntPtr _lastTargetMonitor;
    private double _lastAppliedWidth;

    // Frozen Fluent 2 pill brushes (always legible frosted surfaces on Light & Dark taskbars)
    private static readonly SolidColorBrush DarkTextBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(24, 24, 24));
    private static readonly SolidColorBrush DarkMetaBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(92, 92, 92));
    private static readonly SolidColorBrush LightTextBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(245, 245, 245));
    private static readonly SolidColorBrush LightMetaBrush = ThemeService.CreateFrozenBrush(Color.FromRgb(176, 176, 176));

    private static readonly SolidColorBrush LightPillBg = ThemeService.CreateFrozenBrush(Color.FromArgb(204, 255, 255, 255));
    private static readonly SolidColorBrush LightPillHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(238, 255, 255, 255));
    private static readonly SolidColorBrush LightPillPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(196, 236, 236, 236));
    private static readonly SolidColorBrush LightPillBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(28, 0, 0, 0));
    private static readonly SolidColorBrush LightPillHoverBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(46, 0, 0, 0));

    private static readonly SolidColorBrush DarkPillBg = ThemeService.CreateFrozenBrush(Color.FromArgb(204, 42, 42, 42));
    private static readonly SolidColorBrush DarkPillHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(234, 56, 56, 56));
    private static readonly SolidColorBrush DarkPillPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(196, 32, 32, 32));
    private static readonly SolidColorBrush DarkPillBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(34, 255, 255, 255));
    private static readonly SolidColorBrush DarkPillHoverBorder = ThemeService.CreateFrozenBrush(Color.FromArgb(56, 255, 255, 255));

    private static readonly SolidColorBrush AboveLightBg = ThemeService.CreateFrozenBrush(Color.FromArgb(242, 250, 250, 250));
    private static readonly SolidColorBrush AboveLightHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(252, 255, 255, 255));
    private static readonly SolidColorBrush AboveLightPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(236, 236, 236, 236));
    private static readonly SolidColorBrush AboveDarkBg = ThemeService.CreateFrozenBrush(Color.FromArgb(242, 38, 38, 38));
    private static readonly SolidColorBrush AboveDarkHoverBg = ThemeService.CreateFrozenBrush(Color.FromArgb(250, 52, 52, 52));
    private static readonly SolidColorBrush AboveDarkPressedBg = ThemeService.CreateFrozenBrush(Color.FromArgb(236, 30, 30, 30));

    public IntPtr Handle => _hwnd;

    public static bool IsFullScreenApplicationActive(IntPtr excludeHwnd = default, IntPtr targetMonitor = default) =>
        Win32.IsFullscreenAppActive(excludeHwnd, targetMonitor);

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? RecallRequested;
    public event Action? SettingsRequested;

    private readonly Action _themeChangedHandler;
    private readonly Action _languageChangedHandler;
    private readonly Action<AppSettings> _settingsChangedHandler;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();

        // Lightweight 500ms guard for FollowCursor monitor moves & tray resize auto-align
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
        RootPill.ContextMenuOpening += OnWidgetContextMenuOpening;
        WidgetContextMenu.Opened += OnWidgetContextMenuOpened;
        WidgetContextMenu.Closed += OnWidgetContextMenuClosed;
        Closed += OnClosed;

        ThemeService.Instance.ThemeChanged += _themeChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        _settings.SettingsChanged += _settingsChangedHandler;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        RefreshTaskbarLuminanceCache();
        ApplyLocalization();
        ApplyTheme();
    }

    public void SetPausedState(bool paused)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => SetPausedState(paused));
            return;
        }

        _isPaused = paused;
        RootPill.Opacity = paused ? 0.72 : 1.0;
        RefreshMetaText();
        UpdatePosition(force: true);
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

        // Place accurately BEFORE first paint so window never flashes at top-left
        UpdatePosition(force: true);
        SyncHooksAndTimer();
    }

    private void InstallForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        try
        {
            _winEventDelegate = OnForegroundWindowChanged;
            _foregroundHook = Win32.SetWinEventHook(
                Win32.EVENT_SYSTEM_FOREGROUND,
                Win32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32.WINEVENT_OUTOFCONTEXT);
        }
        catch
        {
        }
    }

    private void UninstallForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            try { Win32.UnhookWinEvent(_foregroundHook); } catch { }
            _foregroundHook = IntPtr.Zero;
            _winEventDelegate = null;
        }
    }

    private void OnForegroundWindowChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (Dispatcher.HasShutdownStarted || !_settings.Current.ShowTaskbarWidget) return;
        Dispatcher.BeginInvoke(() => HandleForegroundChange(hwnd));
    }

    private void HandleForegroundChange(IntPtr fgHwnd)
    {
        var cfg = _settings.Current;
        if (!cfg.ShowTaskbarWidget) return;

        bool shouldHide = ShouldHideByGuard(cfg);
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
            EnsureTopmost();
            return;
        }

        // User clicked the taskbar -> immediately re-assert topmost so widget never vanishes behind taskbar!
        if (Win32.IsTaskbarWindow(fgHwnd))
        {
            EnsureTopmost();
            _ = Task.Delay(45).ContinueWith(_ =>
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (IsVisible && !_isHiddenByGuard && WidgetContextMenu?.IsOpen != true)
                        {
                            EnsureTopmost();
                        }
                    });
                }
            });
        }
    }

    private void EnsureTopmost()
    {
        if (_hwnd != IntPtr.Zero && IsVisible && WidgetContextMenu?.IsOpen != true)
        {
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
    }

    private void OnWidgetContextMenuOpening(object sender, ContextMenuEventArgs e) => ActivateForContextMenu();
    private void OnWidgetContextMenuOpened(object sender, RoutedEventArgs e) => ActivateForContextMenu();

    private void ActivateForContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;
        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)(exStyle & ~Win32.WS_EX_NOACTIVATE));
        Win32.SetForegroundWindow(_hwnd);
    }

    private void OnWidgetContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (_hwnd == IntPtr.Zero) return;
        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE,
            (IntPtr)(exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST));
        EnsureTopmost();
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
        else if (msg == Win32.WM_DPICHANGED)
        {
            Dispatcher.BeginInvoke(() => UpdatePosition(force: true));
        }
        else if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdatePosition(force: true);
                EnsureTopmost();
            });
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshTaskbarLuminanceCache();
        ApplyTheme();
        _isHiddenByGuard = ShouldHideByGuard(_settings.Current);
        if (_isHiddenByGuard && IsVisible) Hide();
        UpdatePosition(force: true);
        EnsureTopmost();
        SyncHooksAndTimer();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _guardTimer.Stop();
        UninstallForegroundHook();
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
        RootPill.Opacity = (_isPaused || !cfg.IsMonitoringEnabled) ? 0.72 : 1.0;
        RefreshMetaText();

        if (cfg.ShowTaskbarWidget)
        {
            _isHiddenByGuard = ShouldHideByGuard(cfg);
            if (_isHiddenByGuard)
            {
                if (IsVisible) Hide();
            }
            else if (!IsVisible)
            {
                Show();
            }
            RefreshTaskbarLuminanceCache();
            ApplyTheme();
            UpdatePosition(force: true);
            EnsureTopmost();
            SyncHooksAndTimer();
        }
        else
        {
            _guardTimer.Stop();
            UninstallForegroundHook();
            if (IsVisible) Hide();
        }
        UpdateContextMenuChecks(cfg);
    }

    private void SyncHooksAndTimer()
    {
        if (_settings.Current.ShowTaskbarWidget)
        {
            if (!_guardTimer.IsEnabled) _guardTimer.Start();
            InstallForegroundHook();
        }
        else
        {
            _guardTimer.Stop();
            UninstallForegroundHook();
        }
    }

    public static bool IsTaskbarOffScreen(Win32.RECT tbRect, Win32.RECT monitorRect)
    {
        int visibleW = Math.Min(tbRect.Right, monitorRect.Right) - Math.Max(tbRect.Left, monitorRect.Left);
        int visibleH = Math.Min(tbRect.Bottom, monitorRect.Bottom) - Math.Max(tbRect.Top, monitorRect.Top);
        return visibleW <= 4 || visibleH <= 4;
    }

    public static bool IsTrayRectValid(Win32.RECT trayRect, Win32.RECT tbRect)
    {
        int trayW = trayRect.Right - trayRect.Left;
        int tbW = tbRect.Right - tbRect.Left;
        if (trayW <= 8 || tbW <= 200) return false;
        // System tray always sits on the right side of a horizontal taskbar
        return trayRect.Left > tbRect.Left + 100 && trayRect.Right <= tbRect.Right + 8;
    }

    private bool IsTaskbarAutoHidden(AppSettings cfg, IntPtr targetMonitor)
    {
        if (cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar) return false;
        IntPtr taskbarHwnd = FindTaskbarForMonitor(targetMonitor);
        // If this secondary monitor simply has no taskbar at all, do not hide (we dock above bottom-right instead)
        if (taskbarHwnd == IntPtr.Zero || !Win32.GetWindowRect(taskbarHwnd, out var tbRect)) return false;

        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(targetMonitor, ref mi)) return false;

        return IsTaskbarOffScreen(tbRect, mi.rcMonitor);
    }

    private bool ShouldHideByGuard(AppSettings cfg)
    {
        IntPtr targetMonitor = ResolveTargetMonitor(cfg.WidgetMonitor);
        return Win32.IsFullscreenAppActive(_hwnd, targetMonitor) ||
               (cfg.WidgetPosition != WidgetPositionMode.AboveTaskbar && Win32.IsShellOverlayActive()) ||
               IsTaskbarAutoHidden(cfg, targetMonitor);
    }

    private void GuardTimer_Tick(object? sender, EventArgs e)
    {
        var cfg = _settings.Current;
        if (!cfg.ShowTaskbarWidget)
        {
            _guardTimer.Stop();
            UninstallForegroundHook();
            if (IsVisible) Hide();
            return;
        }

        bool shouldHide = ShouldHideByGuard(cfg);

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
            EnsureTopmost();
            return;
        }

        UpdatePosition(force: false);
        EnsureTopmost();
    }

    private void RefreshTaskbarLuminanceCache()
    {
        _cachedTaskbarIsLight = TaskbarColorDetector.IsTaskbarLightFast();
    }

    private bool ResolveLightPillSurface()
    {
        var cfg = _settings.Current;
        return cfg.WidgetTextColor switch
        {
            WidgetTextColorMode.Light => false, // White text -> Dark frosted pill
            WidgetTextColorMode.Dark => true,   // Dark text -> Light frosted pill
            _ => cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar
                ? !ThemeService.Instance.IsDarkTheme
                : _cachedTaskbarIsLight
        };
    }

    public void ApplyTheme()
    {
        var cfg = _settings.Current;
        bool isLightSurface = ResolveLightPillSurface();
        bool isAbove = cfg.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        if (isAbove)
        {
            if (_isPressed)
            {
                RootPill.Background = isLightSurface ? AboveLightPressedBg : AboveDarkPressedBg;
                RootPill.BorderBrush = isLightSurface ? LightPillHoverBorder : DarkPillHoverBorder;
            }
            else if (_isHovered)
            {
                RootPill.Background = isLightSurface ? AboveLightHoverBg : AboveDarkHoverBg;
                RootPill.BorderBrush = isLightSurface ? LightPillHoverBorder : DarkPillHoverBorder;
            }
            else
            {
                RootPill.Background = isLightSurface ? AboveLightBg : AboveDarkBg;
                RootPill.BorderBrush = isLightSurface ? LightPillBorder : DarkPillBorder;
            }
        }
        else
        {
            if (_isPressed)
            {
                RootPill.Background = isLightSurface ? LightPillPressedBg : DarkPillPressedBg;
                RootPill.BorderBrush = isLightSurface ? LightPillHoverBorder : DarkPillHoverBorder;
            }
            else if (_isHovered)
            {
                RootPill.Background = isLightSurface ? LightPillHoverBg : DarkPillHoverBg;
                RootPill.BorderBrush = isLightSurface ? LightPillHoverBorder : DarkPillHoverBorder;
            }
            else
            {
                RootPill.Background = isLightSurface ? LightPillBg : DarkPillBg;
                RootPill.BorderBrush = isLightSurface ? LightPillBorder : DarkPillBorder;
            }
        }

        var fgPrimary = isLightSurface ? DarkTextBrush : LightTextBrush;
        var fgMeta = isLightSurface ? DarkMetaBrush : LightMetaBrush;
        ClipPreviewText.Foreground = fgPrimary;
        ClipMetaText.Foreground = fgMeta;
        ChevronIcon.Foreground = fgMeta;
        ChevronIcon.Opacity = _isHovered ? 0.95 : 0.52;

        ApplyTypeIconBadgePalette(_currentResult?.Type ?? ClipDataType.PlainText, isLightSurface);
    }

    private void ApplyTypeIconBadgePalette(ClipDataType type, bool isLightSurface)
    {
        var (bg, fg) = isLightSurface
            ? type switch
            {
                ClipDataType.HexColor or ClipDataType.Uuid => (Color.FromRgb(243, 232, 255), Color.FromRgb(107, 33, 168)),
                ClipDataType.Json => (Color.FromRgb(232, 243, 255), Color.FromRgb(29, 78, 216)),
                ClipDataType.Url or ClipDataType.TableData => (Color.FromRgb(226, 248, 238), Color.FromRgb(4, 120, 87)),
                ClipDataType.Email => (Color.FromRgb(232, 247, 255), Color.FromRgb(3, 105, 161)),
                ClipDataType.Code or ClipDataType.FileDrop or ClipDataType.FilePath => (Color.FromRgb(254, 244, 222), Color.FromRgb(180, 83, 9)),
                ClipDataType.Image => (Color.FromRgb(253, 238, 246), Color.FromRgb(190, 24, 93)),
                ClipDataType.UnixTimestamp or ClipDataType.IsoDateTime or ClipDataType.MathExpression => (Color.FromRgb(228, 249, 253), Color.FromRgb(14, 116, 144)),
                ClipDataType.Base64 => (Color.FromRgb(236, 240, 255), Color.FromRgb(67, 56, 202)),
                _ => (Color.FromArgb(20, 0, 0, 0), Color.FromRgb(54, 54, 54))
            }
            : type switch
            {
                ClipDataType.HexColor or ClipDataType.Uuid => (Color.FromArgb(52, 139, 92, 246), Color.FromRgb(216, 180, 254)),
                ClipDataType.Json => (Color.FromArgb(52, 59, 130, 246), Color.FromRgb(147, 197, 253)),
                ClipDataType.Url or ClipDataType.TableData => (Color.FromArgb(52, 16, 185, 129), Color.FromRgb(110, 231, 183)),
                ClipDataType.Email => (Color.FromArgb(52, 14, 165, 233), Color.FromRgb(125, 211, 252)),
                ClipDataType.Code or ClipDataType.FileDrop or ClipDataType.FilePath => (Color.FromArgb(52, 245, 158, 11), Color.FromRgb(253, 224, 71)),
                ClipDataType.Image => (Color.FromArgb(52, 236, 72, 153), Color.FromRgb(249, 168, 212)),
                ClipDataType.UnixTimestamp or ClipDataType.IsoDateTime or ClipDataType.MathExpression => (Color.FromArgb(52, 6, 182, 212), Color.FromRgb(103, 232, 249)),
                ClipDataType.Base64 => (Color.FromArgb(52, 99, 102, 241), Color.FromRgb(165, 180, 252)),
                _ => (Color.FromArgb(30, 255, 255, 255), Color.FromRgb(235, 235, 235))
            };

        IconBadge.Background = ThemeService.CreateFrozenBrush(bg);
        FluentTypeIcon.Foreground = ThemeService.CreateFrozenBrush(fg);
    }

    public void ApplyLocalization()
    {
        if (_currentResult == null)
        {
            ClipPreviewText.Text = "ClipFlyout";
            RootPill.ToolTip = _loc.Get("Widget_ClickHint");
        }
        RefreshMetaText();

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

    private void RefreshMetaText()
    {
        if (_isPaused || !_settings.Current.IsMonitoringEnabled)
        {
            ClipMetaText.Text = _loc.Get("Widget_Paused");
            return;
        }

        if (_currentResult == null)
        {
            ClipMetaText.Text = _loc.Get("Widget_Empty");
            return;
        }

        string meta = _currentResult.Type switch
        {
            ClipDataType.HexColor => _loc.Get("Type_HexColor"),
            ClipDataType.MathExpression => _currentResult.PreviewSubtitle,
            ClipDataType.Image => _currentResult.BadgeText ?? _currentResult.PreviewSubtitle,
            ClipDataType.FileDrop => _currentResult.BadgeText ?? _loc.Get("Type_FileDrop"),
            _ => !string.IsNullOrWhiteSpace(_currentResult.BadgeText)
                ? _currentResult.BadgeText
                : _currentResult.PreviewTitle
        };
        ClipMetaText.Text = meta;
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

    public void ClearClipContent()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ClearClipContent);
            return;
        }

        _currentResult = null;
        ColorBox.Visibility = Visibility.Collapsed;
        ImageThumbBorder.Visibility = Visibility.Collapsed;
        ImageThumbBorder.Background = null;
        IconBadge.Visibility = Visibility.Visible;
        FluentTypeIcon.Text = "\uE8C8";
        ClipPreviewText.Text = "ClipFlyout";
        RefreshMetaText();
        RootPill.ToolTip = _loc.Get("Widget_ClickHint");

        ApplyTheme();
        UpdatePosition(force: true);
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

        IconBadge.Visibility = Visibility.Collapsed;
        ColorBox.Visibility = Visibility.Collapsed;
        ImageThumbBorder.Visibility = Visibility.Collapsed;
        ImageThumbBorder.Background = null;

        if (result.Type == ClipDataType.HexColor && result.ColorValue.HasValue)
        {
            ColorBox.Visibility = Visibility.Visible;
            ColorBox.Background = ThemeService.CreateFrozenBrush(result.ColorValue.Value);
            ClipPreviewText.Text = result.HexColorCode ?? snippet;
        }
        else if (result.Type == ClipDataType.Image && result.ImagePreview != null)
        {
            ImageThumbBorder.Visibility = Visibility.Visible;
            var imgBrush = new ImageBrush(result.ImagePreview) { Stretch = Stretch.UniformToFill };
            if (imgBrush.CanFreeze) imgBrush.Freeze();
            ImageThumbBorder.Background = imgBrush;
            ClipPreviewText.Text = result.PreviewTitle;
        }
        else
        {
            IconBadge.Visibility = Visibility.Visible;
            FluentTypeIcon.Text = FlyoutWindow.GetTypeGlyph(result.Type);
            ClipPreviewText.Text = result.Type == ClipDataType.MathExpression
                ? result.PreviewTitle
                : snippet;
        }

        RefreshMetaText();

        string fullPreview = result.Type == ClipDataType.Image
            ? $"{result.PreviewTitle} ({result.PreviewSubtitle})\n{_loc.Get("Widget_ClickHint")}"
            : $"{result.PreviewTitle}\n{snippet}\n{_loc.Get("Widget_ClickHint")}";
        RootPill.ToolTip = fullPreview;

        ApplyTheme();
        UpdatePosition(force: true);
    }

    private double MeasureDesiredWidgetWidth()
    {
        // Measure unconstrained horizontally so Grid '*' column reports natural text width instead of stretching to MaxWidth
        RootPill.Measure(new Size(double.PositiveInfinity, Height));
        double desired = RootPill.DesiredSize.Width;
        if (!double.IsFinite(desired) || desired <= 0) desired = MinWidth;
        return Math.Clamp(Math.Ceiling(desired + 2), MinWidth, MaxWidth);
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

        // Secondary monitor has no taskbar enabled -> return Zero so we fall back cleanly instead of stealing Monitor 1's taskbar
        return IntPtr.Zero;
    }

    private static bool RectsEqual(Win32.RECT a, Win32.RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    public static (int PhysX, int PhysY, int PhysW, int PhysH, double DipLeft, double DipTop) ComputePhysicalBounds(
        Win32.RECT monitorRect,
        Win32.RECT workRect,
        Win32.RECT tbRect,
        bool hasTaskbar,
        Win32.RECT trayRect,
        bool hasTray,
        double dpi,
        double widgetWidth,
        double widgetHeight,
        WidgetPositionMode positionMode,
        double offsetX)
    {
        if (dpi <= 0) dpi = 1.0;

        double monLeft = monitorRect.Left / dpi;
        double monRight = monitorRect.Right / dpi;
        double monBottom = monitorRect.Bottom / dpi;
        double workBottom = workRect.Bottom / dpi;

        double tbLeft = hasTaskbar ? tbRect.Left / dpi : monLeft;
        double tbTop = hasTaskbar ? tbRect.Top / dpi : workBottom;
        double tbRight = hasTaskbar ? tbRect.Right / dpi : monRight;
        double tbBottom = hasTaskbar ? tbRect.Bottom / dpi : monBottom;
        double tbHeight = Math.Max(40, tbBottom - tbTop);

        double targetLeft;
        double targetTop;

        bool floatAbove = positionMode == WidgetPositionMode.AboveTaskbar || !hasTaskbar;
        if (floatAbove)
        {
            targetLeft = monRight - widgetWidth - 16;
            targetTop = Math.Min(workBottom, tbTop) - widgetHeight - 10;
        }
        else
        {
            targetTop = tbTop + (tbHeight - widgetHeight) / 2.0;

            switch (positionMode)
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
                    if (hasTray && IsTrayRectValid(trayRect, tbRect))
                    {
                        targetLeft = (trayRect.Left / dpi) - widgetWidth - 8;
                    }
                    else
                    {
                        // Secondary taskbar clock area (~116px on Win11)
                        targetLeft = tbRight - widgetWidth - 124;
                    }
                    break;
            }
        }

        targetLeft += offsetX;
        double finalLeft = Math.Clamp(targetLeft, monLeft + 4, Math.Max(monLeft + 4, monRight - widgetWidth - 4));

        int physX = (int)Math.Round(finalLeft * dpi);
        int physY = (int)Math.Round(targetTop * dpi);
        int physW = (int)Math.Round(widgetWidth * dpi);
        int physH = (int)Math.Round(widgetHeight * dpi);

        return (physX, physY, physW, physH, finalLeft, targetTop);
    }

    private void UpdatePosition(bool force)
    {
        if (_isApplyingPosition) return;
        _isApplyingPosition = true;
        try
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
                if (trayHwnd != IntPtr.Zero &&
                    Win32.GetWindowRect(trayHwnd, out trayRect) &&
                    IsTrayRectValid(trayRect, tbRect))
                {
                    hasTray = true;
                }
            }

            double desiredWidth = MeasureDesiredWidgetWidth();

            if (!force &&
                hMonitor == _lastTargetMonitor &&
                Math.Abs(desiredWidth - _lastAppliedWidth) < 1.0 &&
                RectsEqual(tbRect, _lastTaskbarRect) &&
                RectsEqual(trayRect, _lastTrayRect))
            {
                return;
            }

            _lastTargetMonitor = hMonitor;
            _lastTaskbarRect = tbRect;
            _lastTrayRect = trayRect;
            _lastAppliedWidth = desiredWidth;

            if (Math.Abs(Width - desiredWidth) >= 0.5)
            {
                Width = desiredWidth;
            }

            var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (!Win32.GetMonitorInfo(hMonitor, ref mi))
            {
                var wa = SystemParameters.WorkArea;
                Left = Math.Max(wa.Left + 8, wa.Right - desiredWidth - 16 + cfg.WidgetOffsetX);
                Top = wa.Bottom - Height - 8;
                return;
            }

            double dpi = Win32.GetMonitorDpiScale(hMonitor, IntPtr.Zero);
            var bounds = ComputePhysicalBounds(
                mi.rcMonitor,
                mi.rcWork,
                tbRect,
                hasTaskbar,
                trayRect,
                hasTray,
                dpi,
                desiredWidth,
                Height,
                cfg.WidgetPosition,
                cfg.WidgetOffsetX);

            if (_hwnd != IntPtr.Zero)
            {
                Win32.SetWindowPos(_hwnd, IntPtr.Zero, bounds.PhysX, bounds.PhysY, bounds.PhysW, bounds.PhysH,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
            else
            {
                Left = bounds.DipLeft;
                Top = bounds.DipTop;
            }
        }
        finally
        {
            _isApplyingPosition = false;
        }
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
        if (!_isPressed) return;
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
