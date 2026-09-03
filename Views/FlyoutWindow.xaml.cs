using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public partial class FlyoutWindow : Window
{
    private DetectionResult? _currentResult;
    private Storyboard? _showStoryboard;
    private Storyboard? _hideStoryboard;
    private bool _isClosing;
    private IntPtr _hwnd = IntPtr.Zero;
    private long _presentationGeneration;

    public event Action? MouseEntered;
    public event Action? MouseLeft;
    public event Action? CloseRequested;

    public FlyoutWindow()
    {
        InitializeComponent();

        _showStoryboard = TryFindResource("ShowStoryboard") as Storyboard;
        _hideStoryboard = TryFindResource("HideStoryboard") as Storyboard;

        SourceInitialized += OnSourceInitialized;
        MouseEnter += (_, _) => MouseEntered?.Invoke();
        MouseLeave += (_, _) => MouseLeft?.Invoke();

        ThemeService.Instance.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        SettingsService.Instance.SettingsChanged += _ => Dispatcher.Invoke(ApplyTheme);

        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwnd = helper.Handle;

        var exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);

        ApplyHardwareAcrylic();
    }

    private void ApplyHardwareAcrylic()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isDark = ThemeService.Instance.IsDarkTheme;
        Win32.EnableAcrylicBlur(_hwnd, isDark, SettingsService.Instance.Current.OpacityPercent);
    }

    public void ApplyTheme()
    {
        bool isDark = ThemeService.Instance.IsDarkTheme;
        double opacity = SettingsService.Instance.Current.OpacityPercent;
        var accent = ThemeService.Instance.AccentColor;

        ApplyHardwareAcrylic();

        if (isDark)
        {
            // Windows 11 Deep Smoky Frosted Acrylic with user opacity
            RootCard.Background = Brushes.Transparent;
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(105, 255, 255, 255));
            InnerHighlightBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255));

            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            // Translucent preview panel (never completely opaque black)
            TextPreviewPanel.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromArgb(110, 30, 41, 59));
            InlineFeedbackBar.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            InlineFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
        else
        {
            // Windows 11 Light Frosted Acrylic with user opacity
            RootCard.Background = Brushes.Transparent;
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 15, 23, 42));
            InnerHighlightBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(155, 255, 255, 255));

            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            // Translucent preview panel in light mode
            TextPreviewPanel.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromArgb(145, 226, 232, 240));
            InlineFeedbackBar.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            InlineFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        }

        if (_currentResult != null)
        {
            ApplyTypeBadgeTheme(_currentResult.Type, isDark);
            StyleActionButtons(isDark);
        }
    }

    private void ApplyTypeBadgeTheme(ClipDataType type, bool isDark)
    {
        if (isDark)
        {
            var (bg, fg) = type switch
            {
                ClipDataType.HexColor => (Color.FromArgb(40, 139, 92, 246), Color.FromRgb(196, 181, 253)),
                ClipDataType.Json => (Color.FromArgb(40, 59, 130, 246), Color.FromRgb(147, 197, 253)),
                ClipDataType.Url => (Color.FromArgb(40, 16, 185, 129), Color.FromRgb(110, 231, 183)),
                ClipDataType.Email => (Color.FromArgb(40, 14, 165, 233), Color.FromRgb(125, 211, 252)),
                ClipDataType.Code => (Color.FromArgb(40, 245, 158, 11), Color.FromRgb(252, 211, 77)),
                ClipDataType.Image => (Color.FromArgb(40, 236, 72, 153), Color.FromRgb(244, 114, 182)),
                ClipDataType.UnixTimestamp => (Color.FromArgb(40, 6, 182, 212), Color.FromRgb(103, 232, 249)),
                ClipDataType.Base64 => (Color.FromArgb(40, 99, 102, 241), Color.FromRgb(165, 180, 252)),
                ClipDataType.TableData => (Color.FromArgb(40, 16, 185, 129), Color.FromRgb(110, 231, 183)),
                _ => (Color.FromArgb(40, 107, 114, 128), Color.FromRgb(209, 213, 219))
            };
            TypeBadgeBorder.Background = new SolidColorBrush(bg);
            TypeBadgeText.Foreground = new SolidColorBrush(fg);
        }
        else
        {
            var (bg, fg) = type switch
            {
                ClipDataType.HexColor => (Color.FromRgb(243, 232, 255), Color.FromRgb(126, 34, 206)),
                ClipDataType.Json => (Color.FromRgb(239, 246, 255), Color.FromRgb(37, 99, 235)),
                ClipDataType.Url => (Color.FromRgb(236, 253, 245), Color.FromRgb(5, 150, 105)),
                ClipDataType.Email => (Color.FromRgb(240, 249, 255), Color.FromRgb(3, 105, 161)),
                ClipDataType.Code => (Color.FromRgb(255, 251, 235), Color.FromRgb(217, 119, 6)),
                ClipDataType.Image => (Color.FromRgb(253, 242, 248), Color.FromRgb(219, 39, 119)),
                ClipDataType.UnixTimestamp => (Color.FromRgb(236, 254, 255), Color.FromRgb(8, 145, 178)),
                ClipDataType.Base64 => (Color.FromRgb(238, 242, 255), Color.FromRgb(79, 70, 229)),
                ClipDataType.TableData => (Color.FromRgb(236, 253, 245), Color.FromRgb(5, 150, 105)),
                _ => (Color.FromRgb(241, 245, 249), Color.FromRgb(71, 85, 105))
            };
            TypeBadgeBorder.Background = new SolidColorBrush(bg);
            TypeBadgeText.Foreground = new SolidColorBrush(fg);
        }
    }

    private void StyleActionButtons(bool isDark)
    {
        bool primaryAssigned = false;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(ActionsItemsControl); i++)
        {
            var child = VisualTreeHelper.GetChild(ActionsItemsControl, i);
            ApplyButtonStylesRecursive(child, isDark, ref primaryAssigned);
        }
    }

    private void ApplyButtonStylesRecursive(DependencyObject parent, bool isDark, ref bool primaryAssigned)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn)
            {
                bool isPrimary = !primaryAssigned;
                primaryAssigned = true;

                if (isPrimary)
                {
                    btn.Height = 30;
                    btn.Padding = new Thickness(12, 0, 12, 0);
                    var accent = ThemeService.Instance.AccentColor;
                    btn.Background = new SolidColorBrush(accent);
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(
                        (byte)Math.Min(255, accent.R + 28),
                        (byte)Math.Min(255, accent.G + 28),
                        (byte)Math.Min(255, accent.B + 28)));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }
                else if (isDark)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(200, 42, 47, 61));
                    btn.BorderBrush = new SolidColorBrush(Color.FromArgb(100, 75, 85, 110));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
                }
                else
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255));
                    btn.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                }
            }
            ApplyButtonStylesRecursive(child, isDark, ref primaryAssigned);
        }
    }

    public void Present(DetectionResult result)
    {
        _presentationGeneration++;
        _currentResult = result;
        _isClosing = false;

        // A new clipboard value can arrive while the previous card is fading
        // out. Remove that clock before displaying the new content; otherwise
        // its Completed callback would hide the new card.
        _hideStoryboard?.Remove(this);
        RootCard.BeginAnimation(OpacityProperty, null);
        RootTransform.BeginAnimation(TranslateTransform.YProperty, null);
        RootCard.Opacity = 1;
        RootTransform.Y = 0;

        ColorPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;

        ActionsItemsControl.Visibility = Visibility.Visible;
        InlineFeedbackBar.Visibility = Visibility.Collapsed;

        HeaderTitleText.Text = result.PreviewTitle;
        HeaderSubtitleText.Text = result.PreviewSubtitle;
        TypeBadgeText.Text = result.BadgeText ?? result.Type.ToString();

        bool isDark = ThemeService.Instance.IsDarkTheme;
        ApplyTypeBadgeTheme(result.Type, isDark);

        switch (result.Type)
        {
            case ClipDataType.HexColor when result.ColorValue.HasValue:
                ColorPreviewPanel.Visibility = Visibility.Visible;
                ColorSwatch.Background = new SolidColorBrush(result.ColorValue.Value);
                ColorHexText.Text = result.HexColorCode ?? result.PreviewTitle;
                ColorValuesText.Text = result.PreviewSubtitle;
                break;

            case ClipDataType.Image when result.ImagePreview != null:
                ImagePreviewPanel.Visibility = Visibility.Visible;
                ImageThumbnail.Source = result.ImagePreview;
                break;

            default:
                TextPreviewPanel.Visibility = Visibility.Visible;
                BodyPreviewText.Text = result.PreviewBody;
                break;
        }

        ActionsItemsControl.ItemsSource = result.AvailableActions;

        Show();
        _showStoryboard?.Begin(this);

        Dispatcher.BeginInvoke(() => StyleActionButtons(isDark));
    }

    public void AnimateHide(Action onCompleted)
    {
        if (_isClosing) return;
        _isClosing = true;
        long generation = _presentationGeneration;

        if (_hideStoryboard != null)
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                _hideStoryboard.Completed -= handler;
                if (generation != _presentationGeneration)
                {
                    return;
                }
                Hide();
                _isClosing = false;
                onCompleted();
            };
            _hideStoryboard.Completed += handler;
            _hideStoryboard.Begin(this);
        }
        else
        {
            Hide();
            _isClosing = false;
            onCompleted();
        }
    }

    public void ShowToastFeedback(string message)
    {
        long generation = _presentationGeneration;
        InlineFeedbackText.Text = message;

        ActionsItemsControl.Visibility = Visibility.Collapsed;
        InlineFeedbackBar.Visibility = Visibility.Visible;

        Task.Delay(950).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                if (generation == _presentationGeneration && IsVisible)
                {
                    CloseRequested?.Invoke();
                }
            });
        });
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActionItem actionItem })
        {
            actionItem.ExecuteAction();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}
