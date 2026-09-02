using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipFlyout.Models;
using ClipFlyout.Native;

namespace ClipFlyout.Views;

public partial class FlyoutWindow : Window
{
    private DetectionResult? _currentResult;
    private Storyboard? _showStoryboard;
    private Storyboard? _hideStoryboard;
    private Storyboard? _toastStoryboard;
    private bool _isClosing;

    public event Action? MouseEntered;
    public event Action? MouseLeft;
    public event Action? CloseRequested;

    public FlyoutWindow()
    {
        InitializeComponent();

        _showStoryboard = TryFindResource("ShowStoryboard") as Storyboard;
        _hideStoryboard = TryFindResource("HideStoryboard") as Storyboard;
        _toastStoryboard = TryFindResource("ToastStoryboard") as Storyboard;

        SourceInitialized += OnSourceInitialized;
        MouseEnter += (_, _) => MouseEntered?.Invoke();
        MouseLeave += (_, _) => MouseLeft?.Invoke();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var exStyle = (int)Win32.GetWindowLongPtr(helper.Handle, Win32.GWL_EXSTYLE);

        // Apply WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW (prevents stealing focus and hiding from alt-tab)
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(helper.Handle, Win32.GWL_EXSTYLE, (IntPtr)exStyle);
    }

    public void Present(DetectionResult result)
    {
        _currentResult = result;
        _isClosing = false;

        // Reset visibility
        ColorPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;
        ToastCard.Opacity = 0;

        HeaderTitleText.Text = result.PreviewTitle;
        HeaderSubtitleText.Text = result.PreviewSubtitle;
        TypeBadgeText.Text = result.BadgeText ?? result.Type.ToString();

        // Badge color themes based on type
        TypeBadgeBorder.Background = result.Type switch
        {
            ClipDataType.HexColor => new SolidColorBrush(Color.FromRgb(168, 85, 247)), // Purple
            ClipDataType.Json => new SolidColorBrush(Color.FromRgb(59, 130, 246)),     // Blue
            ClipDataType.Url => new SolidColorBrush(Color.FromRgb(16, 185, 129)),      // Emerald
            ClipDataType.Code => new SolidColorBrush(Color.FromRgb(245, 158, 11)),     // Amber
            ClipDataType.Image => new SolidColorBrush(Color.FromRgb(236, 72, 153)),    // Pink
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))                    // Gray
        };

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
        ToastMessageText.Text = message;
        _toastStoryboard?.Begin(this);

        Task.Delay(1100).ContinueWith(_ =>
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
