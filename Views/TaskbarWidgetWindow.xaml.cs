using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public event Action<DetectionResult>? FlyoutRequested;
    public event Action? SettingsRequested;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => UpdatePosition();

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

        // Determine taskbar position (usually bottom on Windows 11)
        double left = workArea.Right - Width - 16;
        double top = workArea.Bottom - Height - 6;

        // If taskbar is on bottom and height difference exists:
        if (screenHeight > workArea.Bottom)
        {
            // Dock centered on taskbar height or just at the bottom edge of work area
            top = workArea.Bottom + (screenHeight - workArea.Bottom - Height) / 2.0;
        }

        Left = Math.Max(0, left);
        Top = Math.Max(0, top);
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
            IconText.Text = "📋";
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
            return;
        }

        IconText.Text = result.Type switch
        {
            ClipDataType.HexColor => "🎨",
            ClipDataType.UnixTimestamp => "🕒",
            ClipDataType.Json => "{ }",
            ClipDataType.Url => "🌐",
            ClipDataType.Email => "✉️",
            ClipDataType.Base64 => "🔤",
            ClipDataType.TableData => "📊",
            ClipDataType.Code => "💻",
            ClipDataType.Image => "🖼️",
            _ => "📋"
        };

        string text = result.PreviewTitle;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
        {
            text = result.PreviewBody;
        }
        ClipPreviewText.Text = text.Trim().Replace("\r", " ").Replace("\n", " ");
    }

    public void ApplyTheme()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isDark = _theme.IsDarkTheme;
        bool isTrans = _theme.IsTransparencyEnabled;

        Win32.EnableAcrylicBlur(_hwnd, isDark, 80.0, isTrans);

        if (isDark)
        {
            RootPill.Background = new SolidColorBrush(isTrans ? Color.FromArgb(140, 24, 24, 32) : Color.FromRgb(24, 24, 32));
            RootPill.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            ClipPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
        else
        {
            RootPill.Background = new SolidColorBrush(isTrans ? Color.FromArgb(140, 255, 255, 255) : Color.FromRgb(255, 255, 255));
            RootPill.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
            ClipPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }
    }

    public void ApplyLocalization()
    {
        MenuOpenFlyout.Header = _loc.Get("Tray_Settings_Flyout") != "Tray_Settings_Flyout" ? _loc.Get("Tray_Settings_Flyout") : "フライアウトを開く";
        MenuOpenSettings.Header = _loc.Get("Tray_Settings");
        MenuHideWidget.Header = _loc.Get("Widget_Hide");
        if (_currentResult == null)
        {
            ClipPreviewText.Text = _loc.Get("Widget_Empty");
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
                    UpdatePosition();
                }
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
