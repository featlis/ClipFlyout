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

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public event EventHandler<bool>? Toggled;

    private bool _isPressed;
    private bool _isMouseOver;

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch ts)
        {
            ts.UpdateVisualState(true);
            ts.Toggled?.Invoke(ts, (bool)e.NewValue);
        }
    }

    public ToggleSwitch()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisualState(false);
        ThemeService.Instance.ThemeChanged += () => Dispatcher.Invoke(() => UpdateVisualState(false));
        SettingsService.Instance.SettingsChanged += _ => Dispatcher.Invoke(() => UpdateVisualState(false));
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPressed = true;
        AnimateThumbStretch(true);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPressed)
        {
            _isPressed = false;
            AnimateThumbStretch(false);
            IsOn = !IsOn;
            e.Handled = true;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOver = true;
        ApplyColors(false);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOver = false;
        if (_isPressed)
        {
            _isPressed = false;
            AnimateThumbStretch(false);
        }
        ApplyColors(false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsOn = !IsOn;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void AnimateThumbStretch(bool pressing)
    {
        double targetWidth = pressing ? 17.0 : 12.0;
        var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ThumbBorder.BeginAnimation(WidthProperty, widthAnim);

        // Adjust position slightly when pressing so it expands towards the travel direction
        if (pressing && IsOn)
        {
            var nudgeAnim = new DoubleAnimation(15.0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, nudgeAnim);
        }
        else if (!pressing)
        {
            double targetX = IsOn ? 20.0 : 0.0;
            var snapAnim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, snapAnim);
        }
    }

    public void UpdateVisualState(bool animate)
    {
        double targetX = IsOn ? 20.0 : 0.0;

        if (animate)
        {
            var anim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbTransform.X = targetX;
        }

        ApplyColors(animate);
    }

    public bool? ForceLightMode { get; set; }

    private void ApplyColors(bool animate)
    {
        bool isDark = ForceLightMode == true ? false : (ForceLightMode == false ? true : ThemeService.Instance.IsDarkTheme);
        Color targetBg;
        Color targetBorder;
        Color targetThumb;

        if (IsOn)
        {
            var accent = ThemeService.Instance.AccentColor;
            if (_isMouseOver)
            {
                targetBg = Color.FromRgb(
                    (byte)Math.Min(255, accent.R + 20),
                    (byte)Math.Min(255, accent.G + 20),
                    (byte)Math.Min(255, accent.B + 20)
                );
            }
            else
            {
                targetBg = accent;
            }
            targetBorder = targetBg;
            targetThumb = isDark ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);
        }
        else
        {
            if (isDark)
            {
                targetBg = _isMouseOver ? Color.FromRgb(48, 48, 48) : Color.FromRgb(32, 32, 32);
                targetBorder = _isMouseOver ? Color.FromRgb(180, 180, 180) : Color.FromRgb(140, 140, 140);
                targetThumb = Color.FromRgb(230, 230, 230);
            }
            else
            {
                targetBg = _isMouseOver ? Color.FromRgb(235, 235, 235) : Color.FromRgb(245, 245, 245);
                targetBorder = _isMouseOver ? Color.FromRgb(90, 90, 90) : Color.FromRgb(130, 130, 130);
                targetThumb = Color.FromRgb(80, 80, 80);
            }
        }

        if (animate)
        {
            var bgAnim = new ColorAnimation(targetBg, TimeSpan.FromMilliseconds(160));
            var borderAnim = new ColorAnimation(targetBorder, TimeSpan.FromMilliseconds(160));
            var thumbAnim = new ColorAnimation(targetThumb, TimeSpan.FromMilliseconds(160));

            var bgBrush = new SolidColorBrush(targetBg);
            var borderBrush = new SolidColorBrush(targetBorder);
            var thumbBrush = new SolidColorBrush(targetThumb);

            TrackBorder.Background = bgBrush;
            TrackBorder.BorderBrush = borderBrush;
            ThumbBorder.Background = thumbBrush;

            bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
            thumbBrush.BeginAnimation(SolidColorBrush.ColorProperty, thumbAnim);
        }
        else
        {
            TrackBorder.Background = new SolidColorBrush(targetBg);
            TrackBorder.BorderBrush = new SolidColorBrush(targetBorder);
            ThumbBorder.Background = new SolidColorBrush(targetThumb);
        }
    }
}
