using System;
using System.Collections.Generic;
using System.Linq;
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

public record RecentHistoryEntry(string IconGlyph, string DisplayText, DetectionResult Result);

public partial class FlyoutWindow : Window
{
    private DetectionResult? _currentResult;
    private Storyboard? _showStoryboard;
    private Storyboard? _hideStoryboard;
    private bool _isClosing;
    private IntPtr _hwnd = IntPtr.Zero;
    private long _presentationGeneration;

    public static bool IsFlyoutOpen { get; private set; }

    public event Action? MouseEntered;
    public event Action? MouseLeft;
    public event Action? CloseRequested;
    public event Action? ButtonMouseEntered;
    public event Action? ButtonMouseLeft;
    public event Action? SettingsRequested;
    public event Action<DetectionResult>? HistoryItemSelected;

    private readonly Action _themeChangedHandler;
    private readonly Action<AppSettings> _settingsChangedHandler;

    public FlyoutWindow()
    {
        InitializeComponent();
        try { Icon = AppIconHelper.GetAppIconBitmapSource(32); } catch { }

        _showStoryboard = TryFindResource("ShowStoryboard") as Storyboard;
        _hideStoryboard = TryFindResource("HideStoryboard") as Storyboard;

        RootCard.Opacity = 0;

        SourceInitialized += OnSourceInitialized;

        MouseEnter += (_, _) => MouseEntered?.Invoke();
        MouseLeave += (_, _) => MouseLeft?.Invoke();

        CloseButton.MouseEnter += (_, _) => ButtonMouseEntered?.Invoke();
        CloseButton.MouseLeave += (_, _) => ButtonMouseLeft?.Invoke();
        SettingsButton.MouseEnter += (_, _) => ButtonMouseEntered?.Invoke();
        SettingsButton.MouseLeave += (_, _) => ButtonMouseLeft?.Invoke();

        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Dispatcher.BeginInvoke(ApplyTheme);
            }
        };
        _settingsChangedHandler = _ =>
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Dispatcher.BeginInvoke(ApplyTheme);
            }
        };

        ThemeService.Instance.ThemeChanged += _themeChangedHandler;
        SettingsService.Instance.SettingsChanged += _settingsChangedHandler;
        Closed += (_, _) =>
        {
            IsFlyoutOpen = false;
            ThemeService.Instance.ThemeChanged -= _themeChangedHandler;
            SettingsService.Instance.SettingsChanged -= _settingsChangedHandler;
        };

        // Ensure HWND is created upfront so DWM acrylic and Win32 styles are initialized immediately
        new WindowInteropHelper(this).EnsureHandle();

        ApplyTheme();
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

        ApplyHardwareAcrylic();
    }

    private void ApplyHardwareAcrylic()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isDark = ThemeService.Instance.IsDarkTheme;
        bool isTransparency = ThemeService.Instance.IsTransparencyEnabled;
        Win32.EnableAcrylicBlur(_hwnd, isDark, SettingsService.Instance.Current.OpacityPercent, isTransparency);
    }

    public void ApplyTheme()
    {
        bool isDark = ThemeService.Instance.IsDarkTheme;
        bool isTransparency = ThemeService.Instance.IsTransparencyEnabled;
        double opacity = SettingsService.Instance.Current.OpacityPercent;

        ApplyHardwareAcrylic();

        byte bgAlpha = !isTransparency
            ? (byte)255
            : (byte)Math.Clamp((int)Math.Round((opacity - 20.0) * (200.0 / 80.0) + 30.0), 30, 230);

        if (isDark)
        {
            RootCard.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 20, 20, 26));
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            InnerHighlightBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

            HeaderAppName.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            TimestampText.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            SettingsButton.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            HeroCardBorder.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            HeroCardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            ColorDescText.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));

            TextPreviewPanel.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

            RecentHeader.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromArgb(110, 30, 41, 59));
            InlineFeedbackBar.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            InlineFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
        else
        {
            RootCard.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 255, 255, 255));
            RootCard.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 180, 190, 200));
            InnerHighlightBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

            HeaderAppName.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            TimestampText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            SettingsButton.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            CloseButton.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            HeroCardBorder.Background = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            HeroCardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 148, 163, 184));

            ColorHexText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            ColorDescText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            ColorValuesText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            TextPreviewPanel.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            TextPreviewPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184));
            BodyPreviewText.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            HeaderTitleText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            HeaderSubtitleText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            ImagePreviewBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            ImagePreviewBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184));

            RecentHeader.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            InlineFeedbackBar.Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            InlineFeedbackBar.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 148, 163, 184));
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

    private void ActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.MouseEnter -= ActionButton_MouseEnter;
            btn.MouseEnter += ActionButton_MouseEnter;
            btn.MouseLeave -= ActionButton_MouseLeave;
            btn.MouseLeave += ActionButton_MouseLeave;

            if (btn.DataContext is ActionItem action)
            {
                ApplySingleButtonStyle(btn, action);
            }
        }
    }

    private void ActionButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ButtonMouseEntered?.Invoke();
    private void ActionButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ButtonMouseLeft?.Invoke();

    private void ApplySingleButtonStyle(Button btn, ActionItem action)
    {
        bool isDark = ThemeService.Instance.IsDarkTheme;
        bool isPrimary = action.IsPrimary;

        if (isPrimary)
        {
            var accent = ThemeService.Instance.AccentColor;
            btn.Background = new SolidColorBrush(accent);
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Min(255, accent.R + 25),
                (byte)Math.Min(255, accent.G + 25),
                (byte)Math.Min(255, accent.B + 25)));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }
        else if (isDark)
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
        }
        else
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 148, 163, 184));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        }
    }

    private void StyleActionButtons(bool isDark)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(ActionsItemsControl); i++)
        {
            var child = VisualTreeHelper.GetChild(ActionsItemsControl, i);
            ApplyButtonStylesRecursive(child);
        }
    }

    private void ApplyButtonStylesRecursive(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn && btn.DataContext is ActionItem action)
            {
                ApplySingleButtonStyle(btn, action);
            }
            ApplyButtonStylesRecursive(child);
        }
    }

    public void Present(DetectionResult result)
    {
        _presentationGeneration++;
        _currentResult = result;
        _isClosing = false;
        IsFlyoutOpen = true;

        ApplyTheme();

        _hideStoryboard?.Remove(this);
        RootCard.BeginAnimation(OpacityProperty, null);
        RootTransform.BeginAnimation(TranslateTransform.YProperty, null);
        RootCard.Opacity = 0;
        RootTransform.Y = 12;

        ColorPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;

        ActionsItemsControl.Visibility = Visibility.Visible;
        InlineFeedbackBar.Visibility = Visibility.Collapsed;

        // Header info
        TimestampText.Text = LocalizationService.Instance.Get("Flyout_Time_JustNow");
        SettingsButton.ToolTip = LocalizationService.Instance.Get("Flyout_Settings_Tooltip");
        CloseButton.ToolTip = LocalizationService.Instance.Get("Flyout_Close_Tooltip");
        RecentHeader.Text = LocalizationService.Instance.Get("Flyout_Recent_Header");

        bool isDark = ThemeService.Instance.IsDarkTheme;
        ApplyTypeBadgeTheme(result.Type, isDark);

        switch (result.Type)
        {
            case ClipDataType.HexColor when result.ColorValue.HasValue:
                ColorPreviewPanel.Visibility = Visibility.Visible;
                ColorSwatch.Background = new SolidColorBrush(result.ColorValue.Value);
                ColorHexText.Text = result.HexColorCode ?? result.PreviewTitle;
                ColorDescText.Text = result.PreviewSubtitle ?? LocalizationService.Instance.Get("Type_HexColor_Desc");
                ColorValuesText.Text = result.PreviewBody;
                break;

            case ClipDataType.Image when result.ImagePreview != null:
                ImagePreviewPanel.Visibility = Visibility.Visible;
                ImageThumbnail.Source = result.ImagePreview;
                break;

            default:
                TextPreviewPanel.Visibility = Visibility.Visible;
                HeaderTitleText.Text = result.PreviewTitle;
                HeaderSubtitleText.Text = result.PreviewSubtitle;
                TypeBadgeText.Text = result.BadgeText ?? result.Type.ToString();
                BodyPreviewText.Text = result.PreviewBody;
                break;
        }

        ActionsItemsControl.ItemsSource = result.AvailableActions;

        // Populate Recent History (excluding current result)
        try
        {
            var history = HistoryService.Instance.GetItems()
                .Where(h => !AreResultsEqual(h.Result, result))
                .Take(2)
                .Select(h => new RecentHistoryEntry(
                    GetRecentGlyph(h.Result.Type),
                    $"{h.DisplayTitle}: {h.DisplaySnippet}",
                    h.Result
                ))
                .ToList();

            if (history.Count > 0)
            {
                RecentHistoryItemsControl.ItemsSource = history;
                RecentHistoryPanel.Visibility = Visibility.Visible;
            }
            else
            {
                RecentHistoryPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            RecentHistoryPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string GetRecentGlyph(ClipDataType type) => type switch
    {
        ClipDataType.HexColor => "\uE790",
        ClipDataType.Image => "\uEB9F",
        ClipDataType.Url => "\uE71B",
        ClipDataType.Email => "\uE715",
        ClipDataType.Code => "\uE943",
        ClipDataType.Json => "\uE943",
        ClipDataType.UnixTimestamp => "\uE823",
        _ => "\uE8A5"
    };

    private static bool AreResultsEqual(DetectionResult a, DetectionResult b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Type != b.Type) return false;
        return Equals(a.RawData, b.RawData);
    }

    /// <summary>
    /// Measures the exact rendered height of the entire card content,
    /// forcing layout on children so ItemsControl generates and sizes all action buttons.
    /// </summary>
    public double MeasureContentHeight()
    {
        double width = Width > 0 ? Width : 360;
        RootCard.Measure(new Size(width, double.PositiveInfinity));
        RootCard.Arrange(new Rect(0, 0, width, Math.Max(1, RootCard.DesiredSize.Height)));
        UpdateLayout();
        RootCard.Measure(new Size(width, double.PositiveInfinity));
        return RootCard.DesiredSize.Height;
    }

    public void ShowFlyout()
    {
        Show();
        _showStoryboard?.Begin(this);

        bool isDark = ThemeService.Instance.IsDarkTheme;
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
                IsFlyoutOpen = false;
                _isClosing = false;
                onCompleted();
            };
            _hideStoryboard.Completed += handler;
            _hideStoryboard.Begin(this);
        }
        else
        {
            Hide();
            IsFlyoutOpen = false;
            _isClosing = false;
            onCompleted();
        }
    }

    public void ShowToastFeedback(string message)
    {
        long generation = _presentationGeneration;
        InlineFeedbackText.Text = message;

        ActionsItemsControl.Visibility = Visibility.Collapsed;
        InlineFeedbackBar.Opacity = 0;
        InlineFeedbackBar.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        InlineFeedbackBar.BeginAnimation(UIElement.OpacityProperty, anim);

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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }

    private void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecentHistoryEntry entry })
        {
            HistoryItemSelected?.Invoke(entry.Result);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        IsFlyoutOpen = false;
        CloseRequested?.Invoke();
    }
}
