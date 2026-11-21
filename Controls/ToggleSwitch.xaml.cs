using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipFlyout.Services;

namespace ClipFlyout.Controls;

public partial class ToggleSwitch : UserControl
{
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            nameof(IsOn),
            typeof(bool),
            typeof(ToggleSwitch),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOnChanged));

    public static readonly DependencyProperty ForceLightModeProperty =
        DependencyProperty.Register(
            nameof(ForceLightMode),
            typeof(bool),
            typeof(ToggleSwitch),
            new PropertyMetadata(false, OnThemePropertyChanged));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public bool ForceLightMode
    {
        get => (bool)GetValue(ForceLightModeProperty);
        set => SetValue(ForceLightModeProperty, value);
    }

    public event RoutedEventHandler? Toggled;

    private bool _isPressed;
    private bool _isHovered;
    private bool _subscribed;
    private readonly SolidColorBrush _trackBgBrush = new();
    private readonly SolidColorBrush _trackBorderBrush = new();
    private readonly SolidColorBrush _thumbBrush = new();

    private const double OffX = 0.0;
    private const double OnX = 20.0;

    public ToggleSwitch()
    {
        InitializeComponent();

        TrackBorder.Background = _trackBgBrush;
        TrackBorder.BorderBrush = _trackBorderBrush;
        ThumbBorder.Background = _thumbBrush;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsEnabledChanged += (_, _) => UpdateVisualState(false);
        GotKeyboardFocus += (_, _) => FocusRing.Opacity = 0.7;
        LostKeyboardFocus += (_, _) => FocusRing.Opacity = 0;
        KeyDown += OnKeyDown;

        UpdateVisualState(false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            ThemeService.Instance.ThemeChanged += OnExternalThemeChanged;
            SettingsService.Instance.SettingsChanged += OnExternalSettingsChanged;
            _subscribed = true;
        }
        UpdateVisualState(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            ThemeService.Instance.ThemeChanged -= OnExternalThemeChanged;
            SettingsService.Instance.SettingsChanged -= OnExternalSettingsChanged;
            _subscribed = false;
        }
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        _isPressed = false;
        _isHovered = false;
    }

    private void OnExternalThemeChanged()
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(() => UpdateVisualState(false));
        }
    }

    private void OnExternalSettingsChanged(Models.AppSettings _)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(() => UpdateVisualState(false));
        }
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch sw)
        {
            sw.UpdateVisualState(sw.IsLoaded);
        }
    }

    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch sw)
        {
            sw.UpdateVisualState(false);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsOn = !IsOn;
            Toggled?.Invoke(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        _isPressed = true;
        CaptureMouse();
        UpdateThumbGeometry(true);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || !_isPressed) return;
        _isPressed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();

        var pos = e.GetPosition(RootGrid);
        bool inside = pos.X >= -4 && pos.X <= RootGrid.ActualWidth + 4 &&
                      pos.Y >= -4 && pos.Y <= RootGrid.ActualHeight + 4;

        if (inside)
        {
            IsOn = !IsOn;
            Toggled?.Invoke(this, new RoutedEventArgs());
        }
        else
        {
            UpdateVisualState(true);
        }

        e.Handled = true;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        UpdateVisualState(true);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        if (!_isPressed)
        {
            UpdateVisualState(true);
        }
    }

    private void UpdateThumbGeometry(bool animate)
    {
        double targetWidth;
        double targetHeight;
        double targetTop;
        double targetX;

        if (_isPressed)
        {
            targetWidth = 17.0;
            targetHeight = 14.0;
            targetTop = 2.0;
            targetX = IsOn ? (OnX - 5.0) : OffX;
        }
        else if (_isHovered)
        {
            targetWidth = 14.0;
            targetHeight = 14.0;
            targetTop = 2.0;
            targetX = IsOn ? (OnX - 2.0) : OffX;
        }
        else
        {
            targetWidth = 12.0;
            targetHeight = 12.0;
            targetTop = 3.0;
            targetX = IsOn ? OnX : OffX;
        }

        ThumbBorder.CornerRadius = new CornerRadius(targetHeight / 2.0);
        Canvas.SetTop(ThumbBorder, targetTop);

        if (animate)
        {
            var dur = TimeSpan.FromMilliseconds(160);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            ThumbBorder.BeginAnimation(WidthProperty, new DoubleAnimation(targetWidth, dur) { EasingFunction = ease });
            ThumbBorder.BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, dur) { EasingFunction = ease });
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(targetX, dur) { EasingFunction = ease });
        }
        else
        {
            ThumbBorder.BeginAnimation(WidthProperty, null);
            ThumbBorder.BeginAnimation(HeightProperty, null);
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbBorder.Width = targetWidth;
            ThumbBorder.Height = targetHeight;
            ThumbTransform.X = targetX;
        }
    }

    private void UpdateVisualState(bool animate)
    {
        bool isDark = !ForceLightMode && ThemeService.Instance.IsDarkTheme;
        Color accent = ThemeService.Instance.AccentColor;
        RootGrid.Opacity = IsEnabled ? 1.0 : 0.45;

        Color targetTrackBg;
        Color targetTrackBorder;
        Color targetThumb;

        if (IsOn)
        {
            targetTrackBg = _isPressed
                ? ThemeService.AdjustBrightness(accent, 0.84)
                : (_isHovered ? ThemeService.AdjustBrightness(accent, isDark ? 1.12 : 0.92) : accent);
            targetTrackBorder = targetTrackBg;
            targetThumb = ThemeService.GetContrastingForeground(accent);
        }
        else
        {
            if (isDark)
            {
                targetTrackBg = _isHovered ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(10, 255, 255, 255);
                targetTrackBorder = _isHovered ? Color.FromArgb(180, 255, 255, 255) : Color.FromArgb(135, 255, 255, 255);
                targetThumb = _isHovered ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(200, 255, 255, 255);
            }
            else
            {
                targetTrackBg = _isHovered ? Color.FromArgb(16, 0, 0, 0) : Color.FromArgb(6, 0, 0, 0);
                targetTrackBorder = _isHovered ? Color.FromArgb(165, 0, 0, 0) : Color.FromArgb(120, 0, 0, 0);
                targetThumb = _isHovered ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(155, 0, 0, 0);
            }
        }

        if (animate)
        {
            var dur = TimeSpan.FromMilliseconds(140);
            _trackBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetTrackBg, dur));
            _trackBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetTrackBorder, dur));
            _thumbBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetThumb, dur));
        }
        else
        {
            _trackBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _trackBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _thumbBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _trackBgBrush.Color = targetTrackBg;
            _trackBorderBrush.Color = targetTrackBorder;
            _thumbBrush.Color = targetThumb;
        }

        UpdateThumbGeometry(animate);
    }
}
