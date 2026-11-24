using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public record RecentHistoryEntry(string IconGlyph, string DisplayText, DetectionResult Result, bool IsPinned)
{
    public Visibility PinVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;
}

public partial class FlyoutWindow : Window
{
    private DetectionResult? _currentResult;
    private List<ActionItem> _currentActions = [];
    private int _selectedActionIndex = -1;
    private bool _isClosing;
    private IntPtr _hwnd = IntPtr.Zero;
    private long _presentationGeneration;

    // Cache last DWM backdrop parameters so Present() never redundantly re-applies Win32 acrylic
    private bool? _lastDwmDark;
    private double _lastDwmOpacity = -1;
    private bool? _lastDwmTransparency;

    public static bool IsFlyoutOpen { get; private set; }
    public bool IsTextInputActive => HistorySearchContainer.Visibility == Visibility.Visible && HistorySearchBox.IsKeyboardFocused;
    public event Action<bool>? InteractiveInputModeChanged;

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

        RootCard.Opacity = 0;
        SourceInitialized += OnSourceInitialized;

        MouseEnter += (_, _) => MouseEntered?.Invoke();
        MouseLeave += (_, _) => MouseLeft?.Invoke();

        CloseButton.MouseEnter += (_, _) => ButtonMouseEntered?.Invoke();
        CloseButton.MouseLeave += (_, _) => ButtonMouseLeft?.Invoke();
        SettingsButton.MouseEnter += (_, _) => ButtonMouseEntered?.Invoke();
        SettingsButton.MouseLeave += (_, _) => ButtonMouseLeft?.Invoke();
        PinButton.MouseEnter += (_, _) => ButtonMouseEntered?.Invoke();
        PinButton.MouseLeave += (_, _) => ButtonMouseLeft?.Invoke();

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

        ApplyHardwareAcrylic(force: true);
    }

    private void ApplyHardwareAcrylic(bool force = false)
    {
        if (_hwnd == IntPtr.Zero) return;

        bool isDark = ThemeService.Instance.IsDarkTheme;
        bool isTransparency = ThemeService.Instance.IsTransparencyEnabled;
        double opacity = SettingsService.Instance.Current.OpacityPercent;

        if (!force && _lastDwmDark == isDark && _lastDwmTransparency == isTransparency && Math.Abs(_lastDwmOpacity - opacity) < 0.5)
        {
            return;
        }

        _lastDwmDark = isDark;
        _lastDwmTransparency = isTransparency;
        _lastDwmOpacity = opacity;

        Win32.EnableAcrylicBlur(_hwnd, isDark, opacity, isTransparency);
    }

    /// <summary>
    /// Temporarily clears or restores WS_EX_NOACTIVATE so TextBoxes inside the flyout
    /// (such as inline history search or quick edit) can genuinely receive keyboard focus.
    /// </summary>
    public void SetInteractiveInputMode(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;

        int exStyle = (int)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        if (enable)
        {
            exStyle &= ~Win32.WS_EX_NOACTIVATE;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);
            Win32.SetForegroundWindow(_hwnd);
            Activate();
        }
        else
        {
            exStyle |= Win32.WS_EX_NOACTIVATE;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);
        }

        InteractiveInputModeChanged?.Invoke(enable);
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

        // Authentic Win11 Acrylic surface tint (warm neutral dark #242424 / soft light #F9F9F9)
        Color surfaceColor = isDark
            ? Color.FromArgb(bgAlpha, 32, 32, 32)
            : Color.FromArgb(bgAlpha, 249, 249, 249);
        RootCard.Background = ThemeService.CreateFrozenBrush(surfaceColor);

        if (_currentResult != null)
        {
            ApplyTypeBadgeTheme(_currentResult.Type, isDark);
            UpdatePinButtonVisual();
        }
    }

    private void ApplyTypeBadgeTheme(ClipDataType type, bool isDark)
    {
        var (bg, fg) = isDark
            ? type switch
            {
                ClipDataType.HexColor => (Color.FromArgb(46, 139, 92, 246), Color.FromRgb(216, 180, 254)),
                ClipDataType.Json => (Color.FromArgb(46, 59, 130, 246), Color.FromRgb(147, 197, 253)),
                ClipDataType.Url => (Color.FromArgb(46, 16, 185, 129), Color.FromRgb(110, 231, 183)),
                ClipDataType.Email => (Color.FromArgb(46, 14, 165, 233), Color.FromRgb(125, 211, 252)),
                ClipDataType.Code => (Color.FromArgb(46, 245, 158, 11), Color.FromRgb(253, 224, 71)),
                ClipDataType.Image => (Color.FromArgb(46, 236, 72, 153), Color.FromRgb(249, 168, 212)),
                ClipDataType.UnixTimestamp or ClipDataType.IsoDateTime => (Color.FromArgb(46, 6, 182, 212), Color.FromRgb(103, 232, 249)),
                ClipDataType.Base64 => (Color.FromArgb(46, 99, 102, 241), Color.FromRgb(165, 180, 252)),
                ClipDataType.TableData => (Color.FromArgb(46, 16, 185, 129), Color.FromRgb(110, 231, 183)),
                ClipDataType.FileDrop => (Color.FromArgb(46, 234, 179, 8), Color.FromRgb(253, 224, 71)),
                _ => (Color.FromArgb(42, 160, 160, 160), Color.FromRgb(220, 220, 220))
            }
            : type switch
            {
                ClipDataType.HexColor => (Color.FromRgb(243, 232, 255), Color.FromRgb(107, 33, 168)),
                ClipDataType.Json => (Color.FromRgb(235, 245, 255), Color.FromRgb(29, 78, 216)),
                ClipDataType.Url => (Color.FromRgb(230, 250, 241), Color.FromRgb(4, 120, 87)),
                ClipDataType.Email => (Color.FromRgb(236, 248, 255), Color.FromRgb(3, 105, 161)),
                ClipDataType.Code => (Color.FromRgb(254, 246, 225), Color.FromRgb(180, 83, 9)),
                ClipDataType.Image => (Color.FromRgb(253, 240, 247), Color.FromRgb(190, 24, 93)),
                ClipDataType.UnixTimestamp or ClipDataType.IsoDateTime => (Color.FromRgb(232, 251, 254), Color.FromRgb(14, 116, 144)),
                ClipDataType.Base64 => (Color.FromRgb(238, 242, 255), Color.FromRgb(67, 56, 202)),
                ClipDataType.TableData => (Color.FromRgb(230, 250, 241), Color.FromRgb(4, 120, 87)),
                ClipDataType.FileDrop => (Color.FromRgb(254, 249, 225), Color.FromRgb(161, 98, 7)),
                _ => (Color.FromRgb(238, 238, 238), Color.FromRgb(68, 68, 68))
            };

        TypeBadgeBorder.Background = ThemeService.CreateFrozenBrush(bg);
        TypeBadgeText.Foreground = ThemeService.CreateFrozenBrush(fg);
    }

    private void ActionButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.MouseEnter -= ActionButton_MouseEnter;
            btn.MouseEnter += ActionButton_MouseEnter;
            btn.MouseLeave -= ActionButton_MouseLeave;
            btn.MouseLeave += ActionButton_MouseLeave;
        }
    }

    private void ActionButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ButtonMouseEntered?.Invoke();
    private void ActionButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ButtonMouseLeft?.Invoke();

    public void Present(DetectionResult result, bool canPin = true)
    {
        _presentationGeneration++;
        _currentResult = result;
        _selectedActionIndex = -1;
        _isClosing = false;
        IsFlyoutOpen = true;

        SetInteractiveInputMode(false);
        ApplyTheme();

        // Cancel any in-flight animations cleanly without snapping
        RootCard.BeginAnimation(OpacityProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootCard.Opacity = 0;
        RootScale.ScaleX = 0.975;
        RootScale.ScaleY = 0.975;

        ColorPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;

        ActionsScrollViewer.Visibility = Visibility.Visible;
        InlineFeedbackBar.Visibility = Visibility.Collapsed;
        HistorySearchContainer.Visibility = Visibility.Collapsed;
        HistorySearchBox.Text = string.Empty;
        HistorySearchPlaceholder.Visibility = Visibility.Visible;

        var loc = LocalizationService.Instance;
        HeaderIconGlyph.Text = GetTypeGlyph(result.Type);
        HeaderAppName.Text = GetHeaderTypeTitle(result);

        DateTime? recordedAt = HistoryService.Instance.GetTimestamp(result);
        TimestampText.Text = recordedAt.HasValue
            ? loc.FormatRelativeTime(recordedAt.Value)
            : loc.Get("Flyout_Time_JustNow");

        PinButton.Visibility = canPin ? Visibility.Visible : Visibility.Collapsed;
        PinButton.ToolTip = loc.Get("Flyout_Pin_Tooltip");
        SearchToggleButton.ToolTip = loc.Get("Flyout_Search_Tooltip");
        HistorySearchPlaceholder.Text = loc.Get("Flyout_Search_Placeholder");
        SettingsButton.ToolTip = loc.Get("Flyout_Settings_Tooltip");
        CloseButton.ToolTip = loc.Get("Flyout_Close_Tooltip");
        RecentHeader.Text = loc.Get("Flyout_Recent_Header");
        ClearHistoryButton.Content = loc.Get("Flyout_ClearHistory");
        UpdatePinButtonVisual();

        ApplyTypeBadgeTheme(result.Type, ThemeService.Instance.IsDarkTheme);

        switch (result.Type)
        {
            case ClipDataType.HexColor when result.ColorValue.HasValue:
                ColorPreviewPanel.Visibility = Visibility.Visible;
                ColorSwatch.Background = ThemeService.CreateFrozenBrush(result.ColorValue.Value);
                ColorHexText.Text = result.HexColorCode ?? result.PreviewTitle;
                ColorDescText.Text = result.PreviewSubtitle ?? loc.Get("Type_HexColor_Desc");
                ColorValuesText.Text = result.PreviewBody;
                break;

            case ClipDataType.Image when result.ImagePreview != null:
                ImagePreviewPanel.Visibility = Visibility.Visible;
                ImageThumbnail.Source = result.ImagePreview;
                ImageMetaBadge.Text = result.BadgeText ?? result.PreviewSubtitle;
                break;

            default:
                TextPreviewPanel.Visibility = Visibility.Visible;
                HeaderTitleText.Text = result.PreviewTitle;
                HeaderSubtitleText.Text = result.PreviewSubtitle;
                TypeBadgeText.Text = result.BadgeText ?? result.Type.ToString();
                BodyPreviewText.Text = result.PreviewBody;
                break;
        }

        // Attach numeric keyboard shortcut hints (1..9) to actions cleanly
        _currentActions = result.AvailableActions
            .Select((a, idx) => idx < 9 ? a with { ShortcutHint = (idx + 1).ToString() } : a)
            .ToList();
        ActionsItemsControl.ItemsSource = _currentActions;
        ActionsScrollViewer.ScrollToTop();

        RefreshHistoryList(null);
    }

    private void RefreshHistoryList(string? query)
    {
        try
        {
            var items = string.IsNullOrWhiteSpace(query)
                ? HistoryService.Instance.GetItems()
                : HistoryService.Instance.Search(query);

            bool hasAnyHistory = HistoryService.Instance.Count > 1;
            SearchToggleButton.Visibility = hasAnyHistory ? Visibility.Visible : Visibility.Collapsed;

            var entries = items
                .Where(h => _currentResult == null || !HistoryService.AreResultsEqual(h.Result, _currentResult))
                .Take(string.IsNullOrWhiteSpace(query) ? 4 : 8)
                .Select(h => new RecentHistoryEntry(
                    GetTypeGlyph(h.Result.Type),
                    string.IsNullOrWhiteSpace(h.DisplaySnippet) ? h.DisplayTitle : h.DisplaySnippet,
                    h.Result,
                    h.IsPinned
                ))
                .ToList();

            bool hasUnpinned = entries.Any(e => !e.IsPinned);
            var loc = LocalizationService.Instance;
            RecentHeader.Text = (!hasUnpinned && entries.Count > 0)
                ? loc.Get("Flyout_Pinned_Header")
                : loc.Get("Flyout_Recent_Header");
            ClearHistoryButton.Visibility = hasUnpinned ? Visibility.Visible : Visibility.Collapsed;

            if (entries.Count > 0 || !string.IsNullOrWhiteSpace(query))
            {
                RecentHistoryItemsControl.ItemsSource = entries;
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

    private void UpdatePinButtonVisual()
    {
        bool pinned = HistoryService.Instance.IsPinned(_currentResult);
        PinButtonGlyph.Text = pinned ? "\uE840" : "\uE718"; // Pinned solid vs Pin outline
        PinButtonGlyph.Foreground = pinned
            ? (TryFindResource("FluentAccentBrush") as Brush ?? Brushes.DodgerBlue)
            : (TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
    }

    private static string GetHeaderTypeTitle(DetectionResult result)
    {
        var loc = LocalizationService.Instance;
        return result.Type switch
        {
            ClipDataType.HexColor => loc.Get("Type_HexColor"),
            ClipDataType.Json => result.BadgeText == "JWT" ? loc.Get("Type_Jwt") : loc.Get("Type_Json"),
            ClipDataType.Url => loc.Get("Type_Url"),
            ClipDataType.Email => loc.Get("Type_Email"),
            ClipDataType.Code => loc.Get("Type_Code"),
            ClipDataType.Image => loc.Get("Type_Image"),
            ClipDataType.UnixTimestamp => loc.Get("Type_Timestamp"),
            ClipDataType.IsoDateTime => loc.Get("Type_IsoDateTime"),
            ClipDataType.Base64 => loc.Get("Type_Base64"),
            ClipDataType.TableData => loc.Get("Type_TableData"),
            ClipDataType.FileDrop => loc.Get("Type_FileDrop"),
            _ => loc.Get("Type_PlainText")
        };
    }

    public static string GetTypeGlyph(ClipDataType type) => type switch
    {
        ClipDataType.HexColor => "\uE790",
        ClipDataType.Image => "\uEB9F",
        ClipDataType.Url => "\uE71B",
        ClipDataType.Email => "\uE715",
        ClipDataType.Code => "\uE943",
        ClipDataType.Json => "\uE943",
        ClipDataType.UnixTimestamp => "\uE823",
        ClipDataType.IsoDateTime => "\uE787",
        ClipDataType.Base64 => "\uE785",
        ClipDataType.TableData => "\uE8FD",
        ClipDataType.FileDrop => "\uE8B7",
        _ => "\uE8C8"
    };

    public double MeasureContentHeight()
    {
        double width = Width > 0 ? Width : 356;
        RootCard.Measure(new Size(width, double.PositiveInfinity));
        RootCard.Arrange(new Rect(0, 0, width, Math.Max(1, RootCard.DesiredSize.Height)));
        UpdateLayout();
        RootCard.Measure(new Size(width, double.PositiveInfinity));
        return RootCard.DesiredSize.Height;
    }

    public void ShowFlyout()
    {
        Show();

        var dur = TimeSpan.FromMilliseconds(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        RootCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.975, 1.0, dur) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.975, 1.0, dur) { EasingFunction = ease });
    }

    public void AnimateHide(Action onCompleted)
    {
        if (_isClosing) return;
        _isClosing = true;
        SetInteractiveInputMode(false);
        long generation = _presentationGeneration;

        var dur = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        // Animate from current opacity (no upward snap if interrupted mid-entrance!)
        var opacityAnim = new DoubleAnimation(RootCard.Opacity, 0.0, dur) { EasingFunction = ease };
        var scaleXAnim = new DoubleAnimation(RootScale.ScaleX, 0.98, dur) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(RootScale.ScaleY, 0.98, dur) { EasingFunction = ease };

        opacityAnim.Completed += (_, _) =>
        {
            if (generation != _presentationGeneration) return;
            Hide();
            IsFlyoutOpen = false;
            _isClosing = false;
            onCompleted();
        };

        RootCard.BeginAnimation(OpacityProperty, opacityAnim);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    public void MoveSelection(int delta)
    {
        if (_currentActions.Count == 0) return;

        if (_selectedActionIndex < 0)
        {
            _selectedActionIndex = delta > 0 ? 0 : _currentActions.Count - 1;
        }
        else
        {
            _selectedActionIndex = (_selectedActionIndex + delta + _currentActions.Count) % _currentActions.Count;
        }

        UpdateSelectedActionVisual();
    }

    public bool ExecuteSelectedAction()
    {
        if (_currentActions.Count == 0) return false;
        int idx = _selectedActionIndex >= 0 && _selectedActionIndex < _currentActions.Count ? _selectedActionIndex : 0;
        _currentActions[idx].ExecuteAction();
        return true;
    }

    public bool ExecuteActionByIndex(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= _currentActions.Count) return false;
        _currentActions[zeroBasedIndex].ExecuteAction();
        return true;
    }

    private void UpdateSelectedActionVisual()
    {
        var accentBrush = TryFindResource("FluentAccentBrush") as Brush ?? Brushes.DodgerBlue;
        var onAccentBrush = TryFindResource("FluentTextOnAccentBrush") as Brush ?? Brushes.White;
        var defaultBorder = TryFindResource("ControlStrokeDefaultBrush") as Brush ?? Brushes.Transparent;

        for (int i = 0; i < ActionsItemsControl.Items.Count; i++)
        {
            if (ActionsItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter cp &&
                VisualTreeHelper.GetChildrenCount(cp) > 0 &&
                VisualTreeHelper.GetChild(cp, 0) is Button btn)
            {
                bool isSelected = i == _selectedActionIndex;
                bool isPrimary = _currentActions[i].IsPrimary;
                btn.BorderThickness = new Thickness(isSelected ? 2 : 1);
                if (isSelected)
                {
                    // High-contrast selection ring on primary accent button
                    btn.BorderBrush = isPrimary ? onAccentBrush : accentBrush;
                    btn.BringIntoView();
                }
                else
                {
                    btn.ClearValue(BorderBrushProperty);
                    btn.BorderBrush = isPrimary ? Brushes.Transparent : defaultBorder;
                }
            }
        }
    }

    public void ShowToastFeedback(string message, bool isError = false)
    {
        long generation = _presentationGeneration;
        bool err = isError ||
                   message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("QR Error:", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("Save Error:", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("Image Decode Error:", StringComparison.OrdinalIgnoreCase);

        InlineFeedbackIcon.Text = err ? "\uE7BA" : "\uE73E";
        InlineFeedbackIcon.Foreground = TryFindResource(err ? "StatusErrorBrush" : "StatusSuccessBrush") as Brush
            ?? (err ? Brushes.IndianRed : Brushes.MediumSeaGreen);
        InlineFeedbackText.Text = message;

        // Keep Visibility.Hidden (NOT Collapsed) so window height NEVER jumps!
        ActionsScrollViewer.Visibility = Visibility.Hidden;
        InlineFeedbackBar.Opacity = 0;
        InlineFeedbackBar.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        InlineFeedbackBar.BeginAnimation(UIElement.OpacityProperty, anim);

        _ = Task.Delay(err ? 1600 : 900).ContinueWith(_ =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(() =>
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

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentResult == null) return;
        HistoryService.Instance.TogglePin(_currentResult);
        UpdatePinButtonVisual();
        RefreshHistoryList(HistorySearchBox.Text);
    }

    private void SearchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistorySearchContainer.Visibility == Visibility.Visible)
        {
            HistorySearchBox.Text = string.Empty;
            HistorySearchContainer.Visibility = Visibility.Collapsed;
            SetInteractiveInputMode(false);
        }
        else
        {
            HistorySearchContainer.Visibility = Visibility.Visible;
            SetInteractiveInputMode(true);
            Keyboard.Focus(HistorySearchBox);
        }
    }

    private void HistorySearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SetInteractiveInputMode(true);
    }

    private void HistorySearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (HistorySearchContainer.Visibility == Visibility.Visible && string.IsNullOrEmpty(HistorySearchBox.Text))
        {
            SetInteractiveInputMode(false);
        }
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HistorySearchPlaceholder.Visibility = string.IsNullOrEmpty(HistorySearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshHistoryList(HistorySearchBox.Text);
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryService.Instance.Clear(includePinned: false);
        RefreshHistoryList(HistorySearchBox.Text);
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
