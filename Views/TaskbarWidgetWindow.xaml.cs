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
            Interval = TimeSpan.FromMilliseconds(300)
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

    private void SetTaskbarOwner()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Attaching to the primary Shell_TrayWnd forces the OS to treat this window
        // as part of the Shell's Z-band and prevents taskbar click from hiding it.
        IntPtr ownerTaskbar = Win32.FindWindow("Shell_TrayWnd", null);
        if (ownerTaskbar != IntPtr.Zero)
        {
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_HWNDPARENT, ownerTaskbar);
        }
    }

    public void UpdatePosition()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdatePosition);
            return;
        }

        double workLeft;
        double workTop;
        double workRight;
        double workBottom;
        double screenWidth;
        double screenHeight;

        // Discover the taskbar's monitor and effective DPI scale
        IntPtr ownerTaskbar = Win32.FindWindow("Shell_TrayWnd", null);
        IntPtr hMonitor = ownerTaskbar != IntPtr.Zero
            ? Win32.MonitorFromWindow(ownerTaskbar, Win32.MONITOR_DEFAULTTOPRIMARY)
            : IntPtr.Zero;

        var monitorInfo = new Win32.MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<Win32.MONITORINFO>();

        if (hMonitor != IntPtr.Zero && Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            double dpiScale = Win32.GetMonitorDpiScale(hMonitor, _hwnd);
            workLeft = monitorInfo.rcWork.Left / dpiScale;
            workTop = monitorInfo.rcWork.Top / dpiScale;
            workRight = monitorInfo.rcWork.Right / dpiScale;
            workBottom = monitorInfo.rcWork.Bottom / dpiScale;
            screenWidth = (monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left) / dpiScale;
            screenHeight = (monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top) / dpiScale;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            workLeft = workArea.Left;
            workTop = workArea.Top;
            workRight = workArea.Right;
            workBottom = workArea.Bottom;
            screenWidth = SystemParameters.PrimaryScreenWidth;
            screenHeight = SystemParameters.PrimaryScreenHeight;
        }

        var cfg = _settings.Current;
        double offset = cfg.WidgetOffsetX;
        double left;
        double top;

        // Base vertical taskbar alignment (centered in taskbar strip if on bottom)
        double taskbarBottomDock = screenHeight > workBottom
            ? workBottom + (screenHeight - workBottom - Height) / 2.0
            : workBottom - Height - 4;

        switch (cfg.WidgetPosition)
        {
            case WidgetPositionMode.CenterRight:
                left = workLeft + (screenWidth / 2.0) + 120 + offset;
                top = taskbarBottomDock;
                break;

            case WidgetPositionMode.CenterLeft:
                left = workLeft + (screenWidth / 2.0) - Width - 120 + offset;
                top = taskbarBottomDock;
                break;

            case WidgetPositionMode.FarLeft:
                left = workLeft + 180 + offset;
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

        Left = Math.Clamp(left, workLeft + 8, workRight - Width - 8);
        Top = Math.Clamp(top, workTop + 8, screenHeight - Height - 4);

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

        // Seamless overlay styling matching Windows 11 taskbar items
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

        var fg = isLightText
            ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
            : new SolidColorBrush(Color.FromRgb(15, 23, 42));

        ClipPreviewText.Foreground = fg;
        FluentClipboardIcon.Foreground = fg;
        IconBadgeText.Foreground = fg;

        // Apply contrast drop-shadow to guarantee legibility regardless of desktop background
        if (ClipPreviewShadow != null)
        {
            ClipPreviewShadow.Color = isLightText ? Colors.Black : Colors.White;
            ClipPreviewShadow.Opacity = isLightText ? 0.75 : 0.85;
            ClipPreviewShadow.BlurRadius = 3;
            ClipPreviewShadow.ShadowDepth = 0.5;
        }

        if (IconBadgeShadow != null)
        {
            IconBadgeShadow.Color = isLightText ? Colors.Black : Colors.White;
            IconBadgeShadow.Opacity = isLightText ? 0.75 : 0.85;
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

    private void RootPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentResult != null)
        {
            FlyoutRequested?.Invoke(_currentResult);
        }
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
        IntPtr hMonitor = _hwnd != IntPtr.Zero
            ? Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTOPRIMARY)
            : IntPtr.Zero;
        double dpiScale = Win32.GetMonitorDpiScale(hMonitor, _hwnd);
        bool isAboveTaskbar = _settings.Current.WidgetPosition == WidgetPositionMode.AboveTaskbar;

        bool isTaskbarLight = TaskbarColorDetector.IsTaskbarLight(
            Left,
            Top,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            dpiScale,
            isAboveTaskbar);

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

        if (_settings.Current.WidgetTextColor == WidgetTextColorMode.Auto && IsVisible)
        {
            bool currentIsLight = DetectIsLightText();
            if (currentIsLight != _lastDetectedLightText)
            {
                ApplyTheme();
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
                ? Win32.MonitorFromWindow(widgetHwnd, Win32.MONITOR_DEFAULTTOPRIMARY)
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
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOREDRAW
        );
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg)
        {
            // Explorer restarted, re-attach to the new taskbar
            SetTaskbarOwner();
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
