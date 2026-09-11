using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;
using Microsoft.Win32;

namespace ClipFlyout.Views;

public partial class TaskbarWidgetWindow : Window
{
    private IntPtr _hwnd = IntPtr.Zero;
    private DetectionResult? _currentResult;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private bool _isHovered;
    private readonly uint _taskbarCreatedMsg;
    private bool _isContextMenuOpen;
    private bool _isFullScreenSuppressed;
    private bool _lastDetectedLightText;
    private double _lastKnownTrayLeft;
    private Win32.RECT _lastTaskbarRect;
    private int _colorCheckTickCount;

    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _debouncedTopmostTimer;
    private readonly DispatcherTimer _themeTransitionTimer;
    private int _themeTransitionStep;
    private readonly EventHandler _displaySettingsHandler;
    private readonly Action _themeChangedHandler;
    private readonly Action _languageChangedHandler;

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? SettingsRequested;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();
        _taskbarCreatedMsg = Win32.RegisterWindowMessage("TaskbarCreated");

        try { IconImage.Source = AppIconHelper.CreateAppBitmapSource(32); } catch { }

        _topmostTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _topmostTimer.Tick += (_, _) => OnTopmostTimerTick();

        // Debounced timer for Deactivated — lets the shell finish Z-order
        // operations before we restore topmost, preventing visible flicker.
        _debouncedTopmostTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _debouncedTopmostTimer.Tick += (_, _) =>
        {
            _debouncedTopmostTimer.Stop();
            EnsureTopmost();
        };

        // Multi-stage timer for OS theme transition animations
        _themeTransitionTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _themeTransitionTimer.Tick += OnThemeTransitionTick;

        if (WidgetContextMenu != null)
        {
            WidgetContextMenu.Opened += (_, _) =>
            {
                _isContextMenuOpen = true;
                _topmostTimer.Stop();
                _debouncedTopmostTimer.Stop();
                _themeTransitionTimer.Stop();
            };
            WidgetContextMenu.Closed += (_, _) =>
            {
                _isContextMenuOpen = false;
                _topmostTimer.Start();
            };
        }

        _displaySettingsHandler = (_, _) => Dispatcher.Invoke(UpdatePosition);
        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ScheduleThemeTransitionChecks);
            }
        };
        _languageChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ApplyLocalization);
            }
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            try { IconImage.Source = AppIconHelper.CreateAppBitmapSource(32); } catch { }
            UpdatePosition();
            _topmostTimer.Start();
            EnsureTopmost();
        };
        Closed += (_, _) =>
        {
            _topmostTimer.Stop();
            _debouncedTopmostTimer.Stop();
            _themeTransitionTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= _displaySettingsHandler;
            _settings.SettingsChanged -= OnSettingsChanged;
            _theme.ThemeChanged -= _themeChangedHandler;
            _loc.LanguageChanged -= _languageChangedHandler;
        };
        Deactivated += (_, _) =>
        {
            // Debounce: don't call SetWindowPos immediately during shell
            // Z-order changes (taskbar click, start menu, etc.).
            _debouncedTopmostTimer.Stop();
            _debouncedTopmostTimer.Start();
        };

        _settings.SettingsChanged += OnSettingsChanged;
        _theme.ThemeChanged += _themeChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        SystemEvents.DisplaySettingsChanged += _displaySettingsHandler;

        ApplyTheme();
        ApplyLocalization();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwnd = helper.Handle;

        var hwndSource = HwndSource.FromHwnd(_hwnd);
        if (hwndSource?.CompositionTarget != null)
        {
            hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        hwndSource?.AddHook(WndProc);

        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);

        SetTaskbarOwner();

        ApplyTheme();
    }

    private void SetTaskbarOwner(IntPtr ownerTaskbar = default)
    {
        if (_hwnd == IntPtr.Zero) return;

        if (ownerTaskbar == IntPtr.Zero)
        {
            ownerTaskbar = Win32.FindWindow("Shell_TrayWnd", null);
        }

        if (ownerTaskbar != IntPtr.Zero)
        {
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_HWNDPARENT, ownerTaskbar);
        }
    }

    private (IntPtr hMonitor, IntPtr hTaskbar, Win32.MONITORINFO monInfo, double dpiScale) GetTargetMonitorInfo()
    {
        var cfg = _settings.Current;
        IntPtr primaryTaskbar = Win32.FindWindow("Shell_TrayWnd", null);
        IntPtr hMonitor = IntPtr.Zero;
        IntPtr targetTaskbar = primaryTaskbar;

        var allMonitors = Win32.GetAllMonitors();

        switch (cfg.WidgetMonitor)
        {
            case WidgetMonitorTarget.FollowCursor:
                if (Win32.GetCursorPos(out Win32.POINT pt))
                {
                    hMonitor = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
                }
                break;

            case WidgetMonitorTarget.Monitor1:
                if (allMonitors.Count >= 1) hMonitor = allMonitors[0];
                break;

            case WidgetMonitorTarget.Monitor2:
                if (allMonitors.Count >= 2) hMonitor = allMonitors[1];
                break;

            case WidgetMonitorTarget.Monitor3:
                if (allMonitors.Count >= 3) hMonitor = allMonitors[2];
                break;

            case WidgetMonitorTarget.Primary:
            default:
                hMonitor = primaryTaskbar != IntPtr.Zero
                    ? Win32.MonitorFromWindow(primaryTaskbar, Win32.MONITOR_DEFAULTTOPRIMARY)
                    : Win32.MonitorFromWindow(_hwnd != IntPtr.Zero ? _hwnd : IntPtr.Zero, Win32.MONITOR_DEFAULTTOPRIMARY);
                break;
        }

        if (hMonitor == IntPtr.Zero)
        {
            hMonitor = primaryTaskbar != IntPtr.Zero
                ? Win32.MonitorFromWindow(primaryTaskbar, Win32.MONITOR_DEFAULTTOPRIMARY)
                : Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTOPRIMARY);
        }

        if (hMonitor != IntPtr.Zero)
        {
            IntPtr primaryMon = primaryTaskbar != IntPtr.Zero
                ? Win32.MonitorFromWindow(primaryTaskbar, Win32.MONITOR_DEFAULTTOPRIMARY)
                : IntPtr.Zero;

            if (primaryMon != IntPtr.Zero && hMonitor == primaryMon)
            {
                targetTaskbar = primaryTaskbar;
            }
            else
            {
                IntPtr secTray = IntPtr.Zero;
                while ((secTray = Win32.FindWindowEx(IntPtr.Zero, secTray, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                {
                    if (Win32.MonitorFromWindow(secTray, Win32.MONITOR_DEFAULTTONEAREST) == hMonitor)
                    {
                        targetTaskbar = secTray;
                        break;
                    }
                }
            }
        }

        var info = new Win32.MONITORINFO();
        info.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();
        if (hMonitor == IntPtr.Zero || !Win32.GetMonitorInfo(hMonitor, ref info))
        {
            var work = SystemParameters.WorkArea;
            info.rcWork = new Win32.RECT { Left = (int)work.Left, Top = (int)work.Top, Right = (int)work.Right, Bottom = (int)work.Bottom };
            info.rcMonitor = new Win32.RECT { Left = 0, Top = 0, Right = (int)SystemParameters.PrimaryScreenWidth, Bottom = (int)SystemParameters.PrimaryScreenHeight };
        }

        double dpiScale = Win32.GetMonitorDpiScale(IntPtr.Zero, _hwnd);
        return (hMonitor, targetTaskbar, info, dpiScale);
    }

    public void UpdatePosition()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdatePosition);
            return;
        }

        var (hMonitor, targetTaskbar, monitorInfo, dpiScale) = GetTargetMonitorInfo();
        if (targetTaskbar != IntPtr.Zero)
        {
            SetTaskbarOwner(targetTaskbar);
        }

        double workLeft = monitorInfo.rcWork.Left / dpiScale;
        double workTop = monitorInfo.rcWork.Top / dpiScale;
        double workRight = monitorInfo.rcWork.Right / dpiScale;
        double workBottom = monitorInfo.rcWork.Bottom / dpiScale;
        double monLeft = monitorInfo.rcMonitor.Left / dpiScale;
        double monTop = monitorInfo.rcMonitor.Top / dpiScale;
        double monRight = monitorInfo.rcMonitor.Right / dpiScale;
        double monBottom = monitorInfo.rcMonitor.Bottom / dpiScale;
        double screenWidth = monRight - monLeft;
        double screenHeight = monBottom - monTop;

        var cfg = _settings.Current;
        double offset = cfg.WidgetOffsetX;
        double left = 0;
        double top = 0;

        // Base vertical taskbar alignment (centered in taskbar strip)
        double tbTopDip;
        double tbHeightDip;
        if (targetTaskbar != IntPtr.Zero && Win32.GetWindowRect(targetTaskbar, out Win32.RECT tbRect))
        {
            tbTopDip = tbRect.Top / dpiScale;
            tbHeightDip = (tbRect.Bottom - tbRect.Top) / dpiScale;
        }
        else
        {
            tbTopDip = workBottom;
            tbHeightDip = monBottom - workBottom;
            if (tbHeightDip <= 0) tbHeightDip = 48;
        }
        double taskbarBottomDock = tbTopDip + (tbHeightDip - Height) / 2.0;

        bool autoAligned = false;
        if (cfg.WidgetAutoAlign && cfg.WidgetPosition == WidgetPositionMode.TrayLeft && targetTaskbar != IntPtr.Zero)
        {
            IntPtr trayNotifyWnd = Win32.FindWindowEx(targetTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotifyWnd != IntPtr.Zero && Win32.GetWindowRect(trayNotifyWnd, out Win32.RECT trRect))
            {
                double trayLeftDip = trRect.Left / dpiScale;
                _lastKnownTrayLeft = trayLeftDip;
                left = trayLeftDip - Width - 6 + offset;
                top = taskbarBottomDock;
                autoAligned = true;
            }
        }

        if (!autoAligned)
        {
            switch (cfg.WidgetPosition)
            {
                case WidgetPositionMode.CenterRight:
                    left = monLeft + (screenWidth / 2.0) + 120 + offset;
                    top = taskbarBottomDock;
                    break;

                case WidgetPositionMode.CenterLeft:
                    left = monLeft + (screenWidth / 2.0) - Width - 120 + offset;
                    top = taskbarBottomDock;
                    break;

                case WidgetPositionMode.FarLeft:
                    left = monLeft + 180 + offset;
                    top = taskbarBottomDock;
                    break;

                case WidgetPositionMode.AboveTaskbar:
                    left = workRight - Width - 16 + offset;
                    top = workBottom - Height - 8;
                    break;

                case WidgetPositionMode.TrayLeft:
                default:
                    left = workRight - Width - 16 + offset;
                    top = taskbarBottomDock;
                    break;
            }
        }

        Left = Math.Clamp(left, monLeft + 8, monRight - Width - 8);
        Top = Math.Clamp(top, monTop + 8, monBottom - Height - 4);

        if (_hwnd != IntPtr.Zero)
        {
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        ApplyTheme();
        UpdateMenuCheckedState();
    }

    public void UpdateClipContent(DetectionResult? result)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateClipContent(result));
            return;
        }

        _currentResult = result;

        if (_settings.Current.WidgetTextColor == WidgetTextColorMode.Auto)
        {
            bool currentIsLight = DetectIsLightText();
            if (currentIsLight != _lastDetectedLightText)
            {
                ApplyTheme();
            }
        }

        if (result == null)
        {
            FluentClipboardIcon.Visibility = Visibility.Visible;
            IconImage.Visibility = Visibility.Collapsed;
            IconBadgeText.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Collapsed;
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
            return;
        }

        // 1. Icon / Visual indicator - Fluent icon or color swatch for HexColor
        if (result.Type == ClipDataType.HexColor && result.ColorValue.HasValue)
        {
            FluentClipboardIcon.Visibility = Visibility.Collapsed;
            IconImage.Visibility = Visibility.Collapsed;
            IconBadgeText.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Visible;
            ColorBox.Background = new SolidColorBrush(result.ColorValue.Value);
        }
        else
        {
            ColorBox.Visibility = Visibility.Collapsed;
            FluentClipboardIcon.Visibility = Visibility.Visible;
            IconImage.Visibility = Visibility.Collapsed;
            IconBadgeText.Visibility = Visibility.Collapsed;
        }

        // 2. Display actual content (not type names like "プレーンテキスト(XX文字)")
        string displayContent = "";
        if (result.Type == ClipDataType.Image || result.RawData is BitmapSource)
        {
            var bmp = (result.RawData as BitmapSource) ?? result.ImagePreview;
            string imgLabel = _loc.Get("Type_Image");
            displayContent = bmp != null
                ? $"[{imgLabel}: {bmp.PixelWidth}×{bmp.PixelHeight}px]"
                : $"[{imgLabel}]";
        }
        else if (result.Type == ClipDataType.HexColor)
        {
            displayContent = result.HexColorCode ?? result.RawData?.ToString() ?? "";
        }
        else if (result.RawData is string rawStr && !string.IsNullOrWhiteSpace(rawStr))
        {
            displayContent = rawStr.Trim().Replace("\r", " ").Replace("\n", " ");
        }
        else if (!string.IsNullOrWhiteSpace(result.PreviewBody))
        {
            displayContent = result.PreviewBody.Trim().Replace("\r", " ").Replace("\n", " ");
        }
        else
        {
            displayContent = result.PreviewTitle;
        }

        const int MaxWidgetPreviewLength = 18;
        if (displayContent.Length > MaxWidgetPreviewLength)
        {
            displayContent = displayContent.Substring(0, MaxWidgetPreviewLength) + "...";
        }

        ClipPreviewText.Text = displayContent;
    }

    public void ApplyTheme()
    {
        var textColorMode = _settings.Current.WidgetTextColor;
        bool isLightText;

        switch (textColorMode)
        {
            case WidgetTextColorMode.Light:
                isLightText = true;
                break;
            case WidgetTextColorMode.Dark:
                isLightText = false;
                break;
            case WidgetTextColorMode.Auto:
            default:
                isLightText = DetectIsLightText();
                break;
        }

        _lastDetectedLightText = isLightText;

        bool isAboveTaskbar = _settings.Current.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        // Native Windows 11 styling: completely transparent when resting on taskbar,
        // subtle highlight on hover matching system taskbar buttons.
        if (isAboveTaskbar)
        {
            if (_isHovered)
            {
                RootPill.Background = isLightText
                    ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
                RootPill.BorderBrush = isLightText
                    ? new SolidColorBrush(Color.FromArgb(65, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(45, 0, 0, 0));
            }
            else
            {
                RootPill.Background = isLightText
                    ? new SolidColorBrush(Color.FromArgb(24, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(16, 0, 0, 0));
                RootPill.BorderBrush = isLightText
                    ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
            }
        }
        else
        {
            if (_isHovered)
            {
                RootPill.Background = isLightText
                    ? new SolidColorBrush(Color.FromArgb(22, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(16, 0, 0, 0));
                RootPill.BorderBrush = isLightText
                    ? new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
            }
            else
            {
                RootPill.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                RootPill.BorderBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            }
        }

        var fg = isLightText
            ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
            : new SolidColorBrush(Color.FromRgb(15, 23, 42));

        ClipPreviewText.Foreground = fg;
        FluentClipboardIcon.Foreground = fg;
        IconBadgeText.Foreground = fg;

        // Apply contrast drop-shadow only when floating above taskbar. On taskbar, crisp text without shadow.
        if (ClipPreviewShadow != null)
        {
            ClipPreviewShadow.Color = isLightText ? Colors.Black : Colors.White;
            ClipPreviewShadow.Opacity = isAboveTaskbar ? (isLightText ? 0.75 : 0.85) : 0.0;
            ClipPreviewShadow.BlurRadius = 3;
            ClipPreviewShadow.ShadowDepth = 0.5;
        }

        if (IconBadgeShadow != null)
        {
            IconBadgeShadow.Color = isLightText ? Colors.Black : Colors.White;
            IconBadgeShadow.Opacity = isAboveTaskbar ? (isLightText ? 0.75 : 0.85) : 0.0;
            IconBadgeShadow.BlurRadius = 3;
            IconBadgeShadow.ShadowDepth = 0.5;
        }

        UpdateMenuCheckedState();
    }

    private void RootPill_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        ApplyTheme();
    }

    private void RootPill_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        ApplyTheme();
    }

    private void RootPill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            bool isLightText = _lastDetectedLightText;
            RootPill.Background = isLightText
                ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(28, 0, 0, 0));
        }
    }

    private void RootPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ApplyTheme();
        if (_currentResult != null)
        {
            FlyoutRequested?.Invoke(_currentResult);
        }
    }

    public void ApplyLocalization()
    {
        MenuOpenFlyout.Header = _loc.Get("Tray_Settings_Flyout") != "Tray_Settings_Flyout" ? _loc.Get("Tray_Settings_Flyout") : "フライアウトを開く";
        MenuOpenSettings.Header = _loc.Get("Tray_Settings");
        MenuHideWidget.Header = _loc.Get("Widget_Hide");
        MenuPosition.Header = _loc.Get("Widget_Position_Header");

        MenuPosTrayLeft.Header = _loc.Get("Widget_Pos_TrayLeft");
        MenuPosCenterRight.Header = _loc.Get("Widget_Pos_CenterRight");
        MenuPosCenterLeft.Header = _loc.Get("Widget_Pos_CenterLeft");
        MenuPosFarLeft.Header = _loc.Get("Widget_Pos_FarLeft");
        MenuPosAboveTaskbar.Header = _loc.Get("Widget_Pos_AboveTaskbar");

        MenuAutoAlign.Header = _loc.Get("Setting_WidgetAutoAlign");

        MenuMonitor.Header = _loc.Get("Setting_WidgetMonitor");
        MenuMonPrimary.Header = _loc.Get("Widget_Monitor_Primary");
        MenuMonCursor.Header = _loc.Get("Widget_Monitor_Cursor");
        MenuMonDisplay1.Header = _loc.Get("Widget_Monitor_Display1");
        MenuMonDisplay2.Header = _loc.Get("Widget_Monitor_Display2");
        MenuMonDisplay3.Header = _loc.Get("Widget_Monitor_Display3");

        MenuTextColor.Header = _loc.Get("Setting_WidgetTextColor");
        MenuTextColorAuto.Header = _loc.Get("Widget_TextColor_Auto");
        MenuTextColorLight.Header = _loc.Get("Widget_TextColor_Light");
        MenuTextColorDark.Header = _loc.Get("Widget_TextColor_Dark");

        if (_currentResult == null)
        {
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
        }

        UpdateMenuCheckedState();
    }

    private void UpdateMenuCheckedState()
    {
        var pos = _settings.Current.WidgetPosition;
        MenuPosTrayLeft.IsChecked = pos == WidgetPositionMode.TrayLeft;
        MenuPosCenterRight.IsChecked = pos == WidgetPositionMode.CenterRight;
        MenuPosCenterLeft.IsChecked = pos == WidgetPositionMode.CenterLeft;
        MenuPosFarLeft.IsChecked = pos == WidgetPositionMode.FarLeft;
        MenuPosAboveTaskbar.IsChecked = pos == WidgetPositionMode.AboveTaskbar;

        MenuAutoAlign.IsChecked = _settings.Current.WidgetAutoAlign;

        var targetMon = _settings.Current.WidgetMonitor;
        MenuMonPrimary.IsChecked = targetMon == WidgetMonitorTarget.Primary;
        MenuMonCursor.IsChecked = targetMon == WidgetMonitorTarget.FollowCursor;
        MenuMonDisplay1.IsChecked = targetMon == WidgetMonitorTarget.Monitor1;
        MenuMonDisplay2.IsChecked = targetMon == WidgetMonitorTarget.Monitor2;
        MenuMonDisplay3.IsChecked = targetMon == WidgetMonitorTarget.Monitor3;

        var textColorMode = _settings.Current.WidgetTextColor;
        MenuTextColorAuto.IsChecked = textColorMode == WidgetTextColorMode.Auto;
        MenuTextColorLight.IsChecked = textColorMode == WidgetTextColorMode.Light;
        MenuTextColorDark.IsChecked = textColorMode == WidgetTextColorMode.Dark;
    }

    private void MenuPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tagStr)
        {
            if (Enum.TryParse<WidgetPositionMode>(tagStr, out var mode))
            {
                _settings.UpdateSettings(s => s.WidgetPosition = mode);
                UpdatePosition();
            }
        }
    }

    private void MenuAutoAlign_Click(object sender, RoutedEventArgs e)
    {
        _settings.UpdateSettings(s => s.WidgetAutoAlign = MenuAutoAlign.IsChecked);
        UpdatePosition();
    }

    private void MenuMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tagStr)
        {
            if (Enum.TryParse<WidgetMonitorTarget>(tagStr, out var target))
            {
                _settings.UpdateSettings(s => s.WidgetMonitor = target);
                UpdatePosition();
            }
        }
    }

    private void MenuTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tagStr)
        {
            if (Enum.TryParse<WidgetTextColorMode>(tagStr, out var mode))
            {
                _settings.UpdateSettings(s => s.WidgetTextColor = mode);
                ApplyTheme();
            }
        }
    }

    private void OnSettingsChanged(AppSettings cfg)
    {
        Dispatcher.Invoke(() =>
        {
            if (cfg.ShowTaskbarWidget)
            {
                if (!IsVisible)
                {
                    Show();
                }
                UpdatePosition();
            }
            else
            {
                Hide();
            }
        });
    }

    private void MenuOpenFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (_currentResult != null)
        {
            FlyoutRequested?.Invoke(_currentResult);
        }
    }

    private void MenuOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }

    private void MenuHideWidget_Click(object sender, RoutedEventArgs e)
    {
        _settings.UpdateSettings(s => s.ShowTaskbarWidget = false);
    }

    private bool DetectIsLightText()
    {
        var (hMonitor, targetTaskbar, monInfo, dpiScale) = GetTargetMonitorInfo();
        bool isAboveTaskbar = _settings.Current.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        bool isTaskbarLight = TaskbarColorDetector.IsTaskbarLight(
            Left,
            Top,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            dpiScale,
            isAboveTaskbar,
            targetTaskbar);

        return !isTaskbarLight;
    }

    private void OnTopmostTimerTick()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isFullScreen = IsFullScreenApplicationActive(_hwnd);
        if (isFullScreen != _isFullScreenSuppressed)
        {
            _isFullScreenSuppressed = isFullScreen;
            if (isFullScreen)
            {
                Visibility = Visibility.Collapsed;
                return;
            }
            else
            {
                if (_settings.Current.ShowTaskbarWidget)
                {
                    Visibility = Visibility.Visible;
                    UpdatePosition();
                    EnsureTopmost();
                }
            }
        }

        if (_isFullScreenSuppressed) return;

        // FollowCursor monitor tracking: if cursor moved to a different monitor, realign widget
        if (_settings.Current.WidgetMonitor == WidgetMonitorTarget.FollowCursor && Win32.GetCursorPos(out Win32.POINT cursorPt))
        {
            IntPtr cursorMon = Win32.MonitorFromPoint(cursorPt, Win32.MONITOR_DEFAULTTONEAREST);
            IntPtr currentWidgetMon = Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            if (cursorMon != IntPtr.Zero && currentWidgetMon != IntPtr.Zero && cursorMon != currentWidgetMon)
            {
                UpdatePosition();
            }
        }

        // Taskbar autohide detection: hide widget if taskbar has slid offscreen
        var (hMonitor, targetTaskbar, monInfo, dpiScale) = GetTargetMonitorInfo();
        if (targetTaskbar != IntPtr.Zero && Win32.GetWindowRect(targetTaskbar, out Win32.RECT tbRect))
        {
            bool isTaskbarHidden =
                tbRect.Top >= monInfo.rcMonitor.Bottom - 2 ||
                tbRect.Bottom <= monInfo.rcMonitor.Top + 2 ||
                tbRect.Right <= monInfo.rcMonitor.Left + 2 ||
                tbRect.Left >= monInfo.rcMonitor.Right - 2;

            if (isTaskbarHidden)
            {
                if (Visibility != Visibility.Collapsed)
                {
                    Visibility = Visibility.Collapsed;
                }
                return;
            }
            else if (_settings.Current.ShowTaskbarWidget && Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
                UpdatePosition();
            }

            // Sync position with taskbar sliding animations (autohide show/hide)
            if (tbRect.Top != _lastTaskbarRect.Top || tbRect.Bottom != _lastTaskbarRect.Bottom ||
                tbRect.Left != _lastTaskbarRect.Left || tbRect.Right != _lastTaskbarRect.Right)
            {
                _lastTaskbarRect = tbRect;
                UpdatePosition();
            }

            // Auto-align tracking: if tray icons shifted (e.g. icons added/hidden chevron moved), realign
            if (_settings.Current.WidgetAutoAlign && _settings.Current.WidgetPosition == WidgetPositionMode.TrayLeft)
            {
                IntPtr trayNotifyWnd = Win32.FindWindowEx(targetTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayNotifyWnd != IntPtr.Zero && Win32.GetWindowRect(trayNotifyWnd, out Win32.RECT trRect))
                {
                    double trayLeftDip = trRect.Left / dpiScale;
                    if (Math.Abs(trayLeftDip - _lastKnownTrayLeft) > 2.0)
                    {
                        UpdatePosition();
                    }
                }
            }
        }

        if (_settings.Current.WidgetTextColor == WidgetTextColorMode.Auto && IsVisible)
        {
            _colorCheckTickCount++;
            if (_colorCheckTickCount >= 15) // ~450ms
            {
                _colorCheckTickCount = 0;
                bool currentIsLight = DetectIsLightText();
                if (currentIsLight != _lastDetectedLightText)
                {
                    ApplyTheme();
                }
            }
        }

        EnsureTopmost();
    }

    private void ScheduleThemeTransitionChecks()
    {
        _themeTransitionStep = 0;
        _themeTransitionTimer.Stop();
        _themeTransitionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _themeTransitionTimer.Start();
        ApplyTheme();
    }

    private void OnThemeTransitionTick(object? sender, EventArgs e)
    {
        _themeTransitionStep++;
        ApplyTheme();
        if (_themeTransitionStep == 1)
        {
            _themeTransitionTimer.Interval = TimeSpan.FromMilliseconds(350);
        }
        else if (_themeTransitionStep == 2)
        {
            _themeTransitionTimer.Interval = TimeSpan.FromMilliseconds(600);
        }
        else
        {
            _themeTransitionTimer.Stop();
        }
    }

    public static bool IsFullScreenApplicationActive(IntPtr widgetHwnd)
    {
        try
        {
            // 1. Check DirectX / presentation mode
            if (Win32.SHQueryUserNotificationState(out var qState) == 0)
            {
                if (qState == Win32.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                    qState == Win32.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
                {
                    return true;
                }
            }

            // 2. Check active foreground window
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == widgetHwnd)
            {
                return false;
            }

            if (!Win32.IsWindowVisible(fg) || Win32.IsIconic(fg))
            {
                return false;
            }

            if (fg == Win32.GetDesktopWindow() || fg == Win32.GetShellWindow())
            {
                return false;
            }

            Win32.GetWindowThreadProcessId(fg, out uint fgPid);
            if (fgPid == Environment.ProcessId)
            {
                return false;
            }

            var sb = new System.Text.StringBuilder(256);
            Win32.GetClassName(fg, sb, 256);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            IntPtr widgetMonitor = widgetHwnd != IntPtr.Zero
                ? Win32.MonitorFromWindow(widgetHwnd, Win32.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;

            IntPtr fgMonitor = Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST);

            if (widgetMonitor != IntPtr.Zero && fgMonitor != IntPtr.Zero && widgetMonitor != fgMonitor)
            {
                return false;
            }

            IntPtr targetMonitor = widgetMonitor != IntPtr.Zero ? widgetMonitor : fgMonitor;
            if (targetMonitor == IntPtr.Zero)
            {
                return false;
            }

            var monInfo = new Win32.MONITORINFO();
            monInfo.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();
            if (!Win32.GetMonitorInfo(targetMonitor, ref monInfo))
            {
                return false;
            }

            if (!Win32.GetWindowRect(fg, out Win32.RECT fgRect))
            {
                return false;
            }

            return fgRect.Left <= monInfo.rcMonitor.Left &&
                   fgRect.Top <= monInfo.rcMonitor.Top &&
                   fgRect.Right >= monInfo.rcMonitor.Right &&
                   fgRect.Bottom >= monInfo.rcMonitor.Bottom;
        }
        catch
        {
            return false;
        }
    }

    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || _isFullScreenSuppressed) return;
        if (_isContextMenuOpen || WidgetContextMenu?.IsOpen == true || TrayIconService.IsContextMenuActive || FlyoutWindow.IsFlyoutOpen) return;

        IntPtr prevHwnd = Win32.GetWindow(_hwnd, Win32.GW_HWNDPREV);
        if (prevHwnd == IntPtr.Zero)
        {
            return; // Already topmost in Z-order
        }

        Win32.SetWindowPos(
            _hwnd,
            Win32.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE
        );
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg)
        {
            // Explorer restarted, re-attach to the new taskbar
            SetTaskbarOwner();
            UpdatePosition();
            EnsureTopmost();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case Win32.WM_SETTINGCHANGE:
                UpdatePosition();
                ScheduleThemeTransitionChecks();
                break;

            case Win32.WM_WINDOWPOSCHANGING:
                if (!_isFullScreenSuppressed && !_isContextMenuOpen && WidgetContextMenu?.IsOpen != true && !TrayIconService.IsContextMenuActive && !FlyoutWindow.IsFlyoutOpen)
                {
                    var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                    pos.hwndInsertAfter = Win32.HWND_TOPMOST;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
        }
        return IntPtr.Zero;
    }
}
