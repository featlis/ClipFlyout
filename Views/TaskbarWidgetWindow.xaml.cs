using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? SettingsRequested;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            IconImage.Source = AppIconHelper.CreateAppBitmapSource(32);
            UpdatePosition();
        };

        _settings.SettingsChanged += OnSettingsChanged;
        _theme.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        _loc.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.Invoke(UpdatePosition);

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

        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);

        // Make taskbar the owner window so widget stays in front of taskbar even when clicked
        IntPtr taskbarHwnd = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd != IntPtr.Zero)
        {
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_HWNDPARENT, taskbarHwnd);
        }

        hwndSource?.AddHook(WndProc);

        ApplyTheme();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
        {
            var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = Win32.HWND_TOPMOST;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
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

        // 1. Icon / Visual indicator
        if (result.Type == ClipDataType.HexColor && result.ColorValue.HasValue)
        {
            IconImage.Visibility = Visibility.Collapsed;
            IconBadgeText.Visibility = Visibility.Collapsed;
            ColorBox.Visibility = Visibility.Visible;
            ColorBox.Background = new SolidColorBrush(result.ColorValue.Value);
        }
        else
        {
            ColorBox.Visibility = Visibility.Collapsed;
            string glyph = result.Type switch
            {
                ClipDataType.UnixTimestamp => "🕒",
                ClipDataType.Json => "{ }",
                ClipDataType.Url => "🌐",
                ClipDataType.Email => "✉️",
                ClipDataType.Base64 => "🔤",
                ClipDataType.TableData => "📊",
                ClipDataType.Code => "💻",
                ClipDataType.Image => "🖼️",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(glyph))
            {
                IconImage.Visibility = Visibility.Visible;
                IconBadgeText.Visibility = Visibility.Collapsed;
            }
            else
            {
                IconImage.Visibility = Visibility.Collapsed;
                IconBadgeText.Visibility = Visibility.Visible;
                IconBadgeText.Text = glyph;
            }
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

        if (displayContent.Length > 45)
        {
            displayContent = displayContent.Substring(0, 42) + "...";
        }

        ClipPreviewText.Text = displayContent;
    }

    public void ApplyTheme()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isDark = _theme.IsDarkTheme;

        // No acrylic or blur effect on taskbar widget: seamless overlay
        if (_isHovered)
        {
            RootPill.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        }
        else
        {
            RootPill.Background = Brushes.Transparent;
        }

        var fg = isDark
            ? new SolidColorBrush(Color.FromRgb(241, 245, 249))
            : new SolidColorBrush(Color.FromRgb(30, 41, 59));

        ClipPreviewText.Foreground = fg;
        IconBadgeText.Foreground = fg;
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
}
