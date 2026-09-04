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

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        IsOn = !IsOn;
        e.Handled = true;
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

    public void UpdateVisualState(bool animate)
    {
        bool isDark = ThemeService.Instance.IsDarkTheme;
        double targetX = IsOn ? 20.0 : 0.0;

        if (animate)
        {
            var anim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbTransform.X = targetX;
        }

        if (IsOn)
        {
            // Accent ON state
            var accentColor = ThemeService.Instance.AccentColor;
            TrackBorder.Background = new SolidColorBrush(accentColor);
            TrackBorder.BorderBrush = new SolidColorBrush(accentColor);
            ThumbEllipse.Fill = isDark ? Brushes.Black : Brushes.White;
        }
        else
        {
            // Neutral OFF state
            if (isDark)
            {
                TrackBorder.Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
                TrackBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
                ThumbEllipse.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            }
            else
            {
                TrackBorder.Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));
                TrackBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(118, 118, 118));
                ThumbEllipse.Fill = new SolidColorBrush(Color.FromRgb(90, 90, 90));
            }
        }
    }
}
