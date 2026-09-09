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

    private readonly DispatcherTimer _topmostTimer;
    private readonly EventHandler _displaySettingsHandler;

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? SettingsRequested;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();

        try { IconImage.Source = AppIconHelper.CreateAppBitmapSource(32); } catch { }

        _topmostTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _topmostTimer.Tick += (_, _) => EnsureTopmost();

        _displaySettingsHandler = (_, _) => Dispatcher.Invoke(UpdatePosition);

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
            SystemEvents.DisplaySettingsChanged -= _displaySettingsHandler;
        };
        Deactivated += (_, _) => EnsureTopmost();
        LocationChanged += (_, _) => EnsureTopmost();

        _settings.SettingsChanged += OnSettingsChanged;
        _theme.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        _loc.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);
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

        ApplyTheme();
    }

    public void UpdatePosition()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdatePosition);
            return;
        }

        var workArea = SystemParameters.WorkArea;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        var cfg = _settings.Current;

        double offset = cfg.WidgetOffsetX;
        double left;
        double top;

        // Base vertical taskbar alignment (centered in taskbar strip if on bottom)
        double taskbarBottomDock = screenHeight > workArea.Bottom
            ? workArea.Bottom + (screenHeight - workArea.Bottom - Height) / 2.0
            : workArea.Bottom - Height - 4;

        switch (cfg.WidgetPosition)
        {
            case WidgetPositionMode.CenterRight:
                left = (screenWidth / 2.0) + 120 + offset;
                top = taskbarBottomDock;
                break;

            case WidgetPositionMode.CenterLeft:
                left = (screenWidth / 2.0) - Width - 120 + offset;
                top = taskbarBottomDock;
                break;

            case WidgetPositionMode.FarLeft:
                left = workArea.Left + 180 + offset;
                top = taskbarBottomDock;
                break;

            case WidgetPositionMode.AboveTaskbar:
                left = workArea.Right - Width - 16 + offset;
                top = workArea.Bottom - Height - 8;
                break;

            case WidgetPositionMode.TrayLeft:
            default:
                left = workArea.Right - Width - 16 + offset;
                top = taskbarBottomDock;
                break;
        }

        Left = Math.Clamp(left, 8, screenWidth - Width - 8);
        Top = Math.Clamp(top, 8, screenHeight - Height - 4);

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

        if (result == null)
        {
            IconImage.Visibility = Visibility.Visible;
            IconBadgeText.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Collapsed;
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
            return;
        }

        // 1. Icon / Visual indicator - always keep the user's preferred tray icon displayed on widget
        if (result.Type == ClipDataType.HexColor && result.ColorValue.HasValue)
        {
            IconImage.Visibility = Visibility.Visible;
            IconBadgeText.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Visible;
            ColorBox.Background = new SolidColorBrush(result.ColorValue.Value);
        }
        else
        {
            ColorBox.Visibility = Visibility.Collapsed;
            IconImage.Visibility = Visibility.Visible;
            IconBadgeText.Visibility = Visibility.Collapsed;
        }

        // 2. Display actual content (not type names like "プレーンテキスト(XX文字)")
        string displayContent = "";
        if (result.Type == ClipDataType.Image || result.RawData is BitmapSource)
        {
            var bmp = (result.RawData as BitmapSource) ?? result.ImagePreview;
            displayContent = bmp != null
                ? $"[画像: {bmp.PixelWidth}×{bmp.PixelHeight}px]"
                : "[画像]";
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
                // Auto-detect based on actual taskbar pixels / system shell theme
                bool isTaskbarLight = TaskbarColorDetector.IsTaskbarLight(Left + Width / 2, Top + Height / 2);
                isLightText = !isTaskbarLight;
                break;
        }

        // Seamless overlay styling
        if (_isHovered)
        {
            RootPill.Background = isLightText
                ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        }
        else
        {
            RootPill.Background = Brushes.Transparent;
        }

        var fg = isLightText
            ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
            : new SolidColorBrush(Color.FromRgb(15, 23, 42));

        ClipPreviewText.Foreground = fg;
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

    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        if (WidgetContextMenu?.IsOpen == true || TrayIconService.IsContextMenuActive || FlyoutWindow.IsFlyoutOpen) return;

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
        switch (msg)
        {
            case Win32.WM_SETTINGCHANGE:
                UpdatePosition();
                break;

            case Win32.WM_WINDOWPOSCHANGING:
                if (WidgetContextMenu?.IsOpen != true && !TrayIconService.IsContextMenuActive && !FlyoutWindow.IsFlyoutOpen)
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
