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
        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var exStyle = (int)Win32.GetWindowLongPtr(helper.Handle, Win32.GWL_EXSTYLE);

        // Apply WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW (prevents stealing focus and hiding from alt-tab)
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(helper.Handle, Win32.GWL_EXSTYLE, (IntPtr)exStyle);
    }

    public void ApplyTheme()
    {
        bool isDark = ThemeService.Instance.IsDarkTheme;

        if (isDark)
        {
            RootCard.Background = new SolidColorBrush(Color.FromArgb(246, 31, 34, 43));
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            CardShadow.Opacity = 0.38;
            CardShadow.Color = Colors.Black;

            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            TextPreviewPanel.Background = new SolidColorBrush(Color.FromRgb(22, 25, 34));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 45, 60));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromRgb(22, 25, 34));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 45, 60));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            InlineFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
        else
        {
            RootCard.Background = new SolidColorBrush(Color.FromArgb(252, 253, 254, 255));
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240));
            CardShadow.Opacity = 0.14;
            CardShadow.Color = Color.FromRgb(15, 23, 42);

            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            TextPreviewPanel.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromRgb(226, 232, 240));
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
                ClipDataType.Code => (Color.FromArgb(40, 245, 158, 11), Color.FromRgb(252, 211, 77)),
                ClipDataType.Image => (Color.FromArgb(40, 236, 72, 153), Color.FromRgb(244, 114, 182)),
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
                ClipDataType.Code => (Color.FromRgb(255, 251, 235), Color.FromRgb(217, 119, 6)),
                ClipDataType.Image => (Color.FromRgb(253, 242, 248), Color.FromRgb(219, 39, 119)),
                _ => (Color.FromRgb(241, 245, 249), Color.FromRgb(71, 85, 105))
            };
            TypeBadgeBorder.Background = new SolidColorBrush(bg);
            TypeBadgeText.Foreground = new SolidColorBrush(fg);
        }
    }

    private void StyleActionButtons(bool isDark)
    {
        // Update loaded buttons colors
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(ActionsItemsControl); i++)
        {
            var child = VisualTreeHelper.GetChild(ActionsItemsControl, i);
            ApplyButtonStylesRecursive(child, isDark);
        }
    }

    private void ApplyButtonStylesRecursive(DependencyObject parent, bool isDark)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn)
            {
                if (isDark)
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(42, 47, 61));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 90));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
                }
                else
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                }
            }
            ApplyButtonStylesRecursive(child, isDark);
        }
    }

    public void Present(DetectionResult result)
    {
        _currentResult = result;
        _isClosing = false;

        // Reset visibility
        ColorPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;

        // Reset action/feedback state (Buttons visible, feedback hidden)
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

        // Apply styles to newly bound buttons after render
        Dispatcher.BeginInvoke(() => StyleActionButtons(isDark));
    }

    public void AnimateHide(Action onCompleted)
    {
        if (_isClosing) return;
        _isClosing = true;

        if (_hideStoryboard != null)
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                _hideStoryboard.Completed -= handler;
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
        InlineFeedbackText.Text = message;

        // Clean inline transition: hide buttons and show feedback bar (ZERO OVERLAP!)
        ActionsItemsControl.Visibility = Visibility.Collapsed;
        InlineFeedbackBar.Visibility = Visibility.Visible;

        Task.Delay(950).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                CloseRequested?.Invoke();
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
