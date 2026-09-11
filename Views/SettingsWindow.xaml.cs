using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public partial class SettingsWindow : Window
{
    private bool _isInitializing = true;
    private bool _isUpdatingColor = false;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private IntPtr _hwnd = IntPtr.Zero;
    private UpdateRelease? _pendingUpdate;
    private System.Drawing.Icon? _iconBig;
    private System.Drawing.Icon? _iconSmall;
    private readonly Action _languageChangedHandler;
    private readonly Action _themeChangedHandler;

    public SettingsWindow()
    {
        InitializeComponent();
        try
        {
            Icon = AppIconHelper.GetAppIconBitmapSource(64);
            HeaderAppIcon.Source = AppIconHelper.GetAppIconBitmapSource(40);
        }
        catch { }

        SourceInitialized += SettingsWindow_SourceInitialized;

        _languageChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ApplyLocalization);
            }
        };
        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ApplyTheme);
            }
        };

        _loc.LanguageChanged += _languageChangedHandler;
        _theme.ThemeChanged += _themeChangedHandler;

        Loaded += SettingsWindow_Loaded;
        Closed += (_, _) =>
        {
            _loc.LanguageChanged -= _languageChangedHandler;
            _theme.ThemeChanged -= _themeChangedHandler;
            _iconBig?.Dispose();
            _iconSmall?.Dispose();
        };
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwnd = helper.Handle;
        ApplyMicaEffect();

        // Enforce the modern gradient clipboard icon directly on Win32 HWND so Windows Taskbar,
        // Alt+Tab, and titlebar never fall back to old cached shell icons.
        try
        {
            _iconBig = AppIconHelper.CreateAppIcon(64);
            _iconSmall = AppIconHelper.CreateAppIcon(16);

            Win32.SendMessage(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_BIG, _iconBig.Handle);
            Win32.SendMessage(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_SMALL, _iconSmall.Handle);
            Win32.SetClassLongPtr(_hwnd, Win32.GCLP_HICON, _iconBig.Handle);
            Win32.SetClassLongPtr(_hwnd, Win32.GCLP_HICONSM, _iconSmall.Handle);
        }
        catch { }

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_GETICON)
        {
            if ((int)wParam == Win32.ICON_BIG && _iconBig != null)
            {
                handled = true;
                return _iconBig.Handle;
            }
            if ((int)wParam == Win32.ICON_SMALL && _iconSmall != null)
            {
                handled = true;
                return _iconSmall.Handle;
            }
        }
        return IntPtr.Zero;
    }

    private void ApplyMicaEffect()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool isDark = _theme.IsDarkTheme;
        Win32.EnableMica(_hwnd, isDark);
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        PopulateDropdowns();
        LoadSettingsValues();
        ApplyLocalization();
        ApplyTheme();

        HookToggleEvents();

        _isInitializing = false;
    }

    private void PopulateDropdowns()
    {
        // Theme dropdown
        ComboTheme.Items.Clear();
        ComboTheme.Items.Add(new ComboBoxItem { Content = _loc.Get("Theme_System"), Tag = AppThemeMode.System });
        ComboTheme.Items.Add(new ComboBoxItem { Content = _loc.Get("Theme_Light"), Tag = AppThemeMode.Light });
        ComboTheme.Items.Add(new ComboBoxItem { Content = _loc.Get("Theme_Dark"), Tag = AppThemeMode.Dark });

        // Language dropdown
        ComboLanguage.Items.Clear();
        ComboLanguage.Items.Add(new ComboBoxItem { Content = _loc.Get("Lang_Auto"), Tag = AppLanguage.Auto });
        ComboLanguage.Items.Add(new ComboBoxItem { Content = _loc.Get("Lang_Ja"), Tag = AppLanguage.Japanese });
        ComboLanguage.Items.Add(new ComboBoxItem { Content = _loc.Get("Lang_En"), Tag = AppLanguage.English });

        // Widget Position dropdown
        ComboWidgetPos.Items.Clear();
        ComboWidgetPos.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Pos_TrayLeft"), Tag = WidgetPositionMode.TrayLeft });
        ComboWidgetPos.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Pos_CenterRight"), Tag = WidgetPositionMode.CenterRight });
        ComboWidgetPos.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Pos_CenterLeft"), Tag = WidgetPositionMode.CenterLeft });
        ComboWidgetPos.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Pos_FarLeft"), Tag = WidgetPositionMode.FarLeft });
        ComboWidgetPos.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Pos_AboveTaskbar"), Tag = WidgetPositionMode.AboveTaskbar });

        // Widget Text Color dropdown
        ComboWidgetTextColor.Items.Clear();
        ComboWidgetTextColor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_TextColor_Auto"), Tag = WidgetTextColorMode.Auto });
        ComboWidgetTextColor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_TextColor_Light"), Tag = WidgetTextColorMode.Light });
        ComboWidgetTextColor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_TextColor_Dark"), Tag = WidgetTextColorMode.Dark });

        // Widget Monitor dropdown
        ComboWidgetMonitor.Items.Clear();
        ComboWidgetMonitor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Monitor_Primary"), Tag = WidgetMonitorTarget.Primary });
        ComboWidgetMonitor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Monitor_Cursor"), Tag = WidgetMonitorTarget.FollowCursor });
        ComboWidgetMonitor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Monitor_Display1"), Tag = WidgetMonitorTarget.Monitor1 });
        ComboWidgetMonitor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Monitor_Display2"), Tag = WidgetMonitorTarget.Monitor2 });
        ComboWidgetMonitor.Items.Add(new ComboBoxItem { Content = _loc.Get("Widget_Monitor_Display3"), Tag = WidgetMonitorTarget.Monitor3 });

        // Placement dropdown
        ComboPlacement.Items.Clear();
        ComboPlacement.Items.Add(new ComboBoxItem { Content = _loc.Get("Placement_BottomRight"), Tag = FlyoutPlacement.BottomRight });
        ComboPlacement.Items.Add(new ComboBoxItem { Content = _loc.Get("Placement_TopRight"), Tag = FlyoutPlacement.TopRight });
        ComboPlacement.Items.Add(new ComboBoxItem { Content = _loc.Get("Placement_TopLeft"), Tag = FlyoutPlacement.TopLeft });
        ComboPlacement.Items.Add(new ComboBoxItem { Content = _loc.Get("Placement_BottomLeft"), Tag = FlyoutPlacement.BottomLeft });
        ComboPlacement.Items.Add(new ComboBoxItem { Content = _loc.Get("Placement_NearCursor"), Tag = FlyoutPlacement.NearCursor });
    }

    private void LoadSettingsValues()
    {
        var cfg = _settings.Current;

        ToggleMonitoring.IsOn = cfg.IsMonitoringEnabled;
        ToggleStartup.IsOn = cfg.LaunchOnStartup;
        ToggleAutoUpdate.IsOn = cfg.AutomaticallyInstallUpdates;

        SelectComboByTag(ComboTheme, cfg.Theme);
        SelectComboByTag(ComboLanguage, cfg.Language);
        SelectComboByTag(ComboWidgetPos, cfg.WidgetPosition);
        SelectComboByTag(ComboWidgetTextColor, cfg.WidgetTextColor);
        SelectComboByTag(ComboWidgetMonitor, cfg.WidgetMonitor);
        ToggleWidgetAutoAlign.IsOn = cfg.WidgetAutoAlign;
        SelectComboByTag(ComboPlacement, cfg.Placement);

        InitColorPicker(cfg.AccentColor);

        SliderWidgetOffset.Value = cfg.WidgetOffsetX;
        TextWidgetOffsetVal.Text = $"{cfg.WidgetOffsetX:+0;-0;0}px";

        SliderOpacity.Value = cfg.OpacityPercent;
        TextOpacityVal.Text = $"{cfg.OpacityPercent:0}%";

        SliderDuration.Value = cfg.DisplayDurationSeconds;
        TextDurationVal.Text = $"{cfg.DisplayDurationSeconds:0.0}s";

        SliderHoverDuration.Value = cfg.HoverLeaveDurationSeconds;
        TextHoverDurationVal.Text = $"{cfg.HoverLeaveDurationSeconds:0.0}s";

        ToggleDetHex.IsOn = cfg.DetectHexColor;
        ToggleDetTimestamp.IsOn = cfg.DetectTimestamp;
        ToggleDetJson.IsOn = cfg.DetectJson;
        ToggleDetUrl.IsOn = cfg.DetectUrl;
        ToggleDetEmail.IsOn = cfg.DetectEmail;
        ToggleDetBase64.IsOn = cfg.DetectBase64;
        ToggleDetTable.IsOn = cfg.DetectTable;
        ToggleDetCode.IsOn = cfg.DetectCode;
        ToggleDetImage.IsOn = cfg.DetectImage;
        ToggleDetText.IsOn = cfg.DetectPlainText;

        ToggleShowWidget.IsOn = cfg.ShowTaskbarWidget;
        ToggleIgnorePasswords.IsOn = cfg.IgnorePasswordManagers;
        ToggleRecallHotkey.IsOn = cfg.EnableRecallHotkey;

        ToggleCleanUrl.IsOn = cfg.EnableCleanUrl;
        ToggleCaseConverter.IsOn = cfg.EnableCaseConverter;
        ToggleJwt.IsOn = cfg.EnableJwtDetector;
    }

    private void HookToggleEvents()
    {
        ToggleMonitoring.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.IsMonitoringEnabled = val); };
        ToggleStartup.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.LaunchOnStartup = val); };
        ToggleAutoUpdate.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.AutomaticallyInstallUpdates = val); };

        ToggleShowWidget.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.ShowTaskbarWidget = val); };
        ToggleWidgetAutoAlign.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.WidgetAutoAlign = val); };
        ToggleIgnorePasswords.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.IgnorePasswordManagers = val); };
        ToggleRecallHotkey.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.EnableRecallHotkey = val); };

        ToggleDetHex.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectHexColor = val); };
        ToggleDetTimestamp.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectTimestamp = val); };
        ToggleDetJson.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectJson = val); };
        ToggleDetUrl.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectUrl = val); };
        ToggleDetEmail.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectEmail = val); };
        ToggleDetBase64.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectBase64 = val); };
        ToggleDetTable.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectTable = val); };
        ToggleDetCode.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectCode = val); };
        ToggleDetImage.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectImage = val); };
        ToggleDetText.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectPlainText = val); };

        ToggleCleanUrl.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.EnableCleanUrl = val); };
        ToggleCaseConverter.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.EnableCaseConverter = val); };
        ToggleJwt.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.EnableJwtDetector = val); };
    }

    private static void SelectComboByTag(ComboBox combo, object tagValue)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (Equals(item.Tag, tagValue))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void TabButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            TabContentGeneral.Visibility = tag == "0" ? Visibility.Visible : Visibility.Collapsed;
            TabContentAppearance.Visibility = tag == "1" ? Visibility.Visible : Visibility.Collapsed;
            TabContentDetectors.Visibility = tag == "2" ? Visibility.Visible : Visibility.Collapsed;
            TabContentUpdates.Visibility = tag == "3" ? Visibility.Visible : Visibility.Collapsed;
            TabContentCredits.Visibility = tag == "4" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #region Color Picker Logic

    private void InitColorPicker(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            UpdateColorPickerUI(color, hex);
        }
        catch
        {
            UpdateColorPickerUI(Color.FromRgb(0, 120, 212), "#0078D4");
        }
    }

    private void UpdateColorPickerUI(Color color, string hex)
    {
        _isUpdatingColor = true;
        try
        {
            SliderR.Value = color.R;
            SliderG.Value = color.G;
            SliderB.Value = color.B;
            TextRVal.Text = color.R.ToString();
            TextGVal.Text = color.G.ToString();
            TextBVal.Text = color.B.ToString();
            TextHexCode.Text = hex.ToUpperInvariant();
            ColorPreviewBox.Background = new SolidColorBrush(color);
        }
        finally
        {
            _isUpdatingColor = false;
        }
    }

    private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingColor || _isInitializing) return;

        byte r = (byte)Math.Round(SliderR.Value);
        byte g = (byte)Math.Round(SliderG.Value);
        byte b = (byte)Math.Round(SliderB.Value);

        TextRVal.Text = r.ToString();
        TextGVal.Text = g.ToString();
        TextBVal.Text = b.ToString();

        var color = Color.FromRgb(r, g, b);
        string hex = $"#{r:X2}{g:X2}{b:X2}";

        _isUpdatingColor = true;
        try
        {
            TextHexCode.Text = hex;
            ColorPreviewBox.Background = new SolidColorBrush(color);
        }
        finally
        {
            _isUpdatingColor = false;
        }

        _theme.UpdateAccentResource(color);
        _settings.UpdateSettings(s => s.AccentColor = hex);
    }

    private void TextHexCode_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingColor || _isInitializing) return;

        string text = TextHexCode.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;

        if (Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(text);
                _isUpdatingColor = true;
                try
                {
                    SliderR.Value = color.R;
                    SliderG.Value = color.G;
                    SliderB.Value = color.B;
                    TextRVal.Text = color.R.ToString();
                    TextGVal.Text = color.G.ToString();
                    TextBVal.Text = color.B.ToString();
                    ColorPreviewBox.Background = new SolidColorBrush(color);
                }
                finally
                {
                    _isUpdatingColor = false;
                }

                _theme.UpdateAccentResource(color);
                _settings.UpdateSettings(s => s.AccentColor = text.ToUpperInvariant());
            }
            catch { }
        }
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                UpdateColorPickerUI(color, hex);
                _theme.UpdateAccentResource(color);
                _settings.UpdateSettings(s => s.AccentColor = hex);
            }
            catch { }
        }
    }

    #endregion

    private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboTheme.SelectedItem is ComboBoxItem { Tag: AppThemeMode mode })
        {
            _theme.Mode = mode;
            _settings.UpdateSettings(s => s.Theme = mode);
        }
    }

    private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboLanguage.SelectedItem is ComboBoxItem { Tag: AppLanguage lang })
        {
            _loc.CurrentLanguage = lang;
            _settings.UpdateSettings(s => s.Language = lang);
        }
    }

    private void ComboWidgetPos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboWidgetPos.SelectedItem is ComboBoxItem { Tag: WidgetPositionMode pos })
        {
            _settings.UpdateSettings(s => s.WidgetPosition = pos);
        }
    }

    private void ComboWidgetTextColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboWidgetTextColor.SelectedItem is ComboBoxItem { Tag: WidgetTextColorMode mode })
        {
            _settings.UpdateSettings(s => s.WidgetTextColor = mode);
        }
    }

    private void ComboWidgetMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboWidgetMonitor.SelectedItem is ComboBoxItem { Tag: WidgetMonitorTarget target })
        {
            _settings.UpdateSettings(s => s.WidgetMonitor = target);
        }
    }

    private void SliderWidgetOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextWidgetOffsetVal != null)
        {
            int val = (int)Math.Round(e.NewValue);
            TextWidgetOffsetVal.Text = $"{val:+0;-0;0}px";
        }
        if (!_isInitializing)
        {
            _settings.UpdateSettings(s => s.WidgetOffsetX = (int)Math.Round(e.NewValue));
        }
    }

    private void ComboPlacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboPlacement.SelectedItem is ComboBoxItem { Tag: FlyoutPlacement placement })
        {
            _settings.UpdateSettings(s => s.Placement = placement);
        }
    }

    private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextOpacityVal != null)
        {
            TextOpacityVal.Text = $"{e.NewValue:0}%";
        }
        if (!_isInitializing)
        {
            _settings.UpdateSettings(s => s.OpacityPercent = Math.Round(e.NewValue, 0));
        }
    }

    private void SliderDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextDurationVal != null)
        {
            TextDurationVal.Text = $"{e.NewValue:0.0}s";
        }
        if (!_isInitializing)
        {
            _settings.UpdateSettings(s => s.DisplayDurationSeconds = Math.Round(e.NewValue, 1));
        }
    }

    private void SliderHoverDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextHoverDurationVal != null)
        {
            TextHoverDurationVal.Text = $"{e.NewValue:0.0}s";
        }
        if (!_isInitializing)
        {
            _settings.UpdateSettings(s => s.HoverLeaveDurationSeconds = Math.Round(e.NewValue, 1));
        }
    }

    public void ApplyTheme()
    {
        bool isDark = _theme.IsDarkTheme;
        ApplyMicaEffect();

        if (isDark)
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 28, 35));
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));

            HeaderBorder.Background = new SolidColorBrush(Color.FromArgb(200, 22, 24, 30));
            HeaderBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            TabHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            TabHeaderBorder.Background = new SolidColorBrush(Color.FromArgb(160, 22, 24, 30));

            AppHeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            AppHeaderSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            ResetDefaultsButton.Background = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            ResetDefaultsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 88));
            ResetDefaultsButton.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));

            SetCardStyle(CardGeneral, isDark);
            SetCardStyle(CardFlyout, isDark);
            SetCardStyle(CardColorPicker, isDark);
            SetCardStyle(CardDetectors, isDark);
            SetCardStyle(CardUpdates, isDark);
            SetCardStyle(CardAbout, isDark);
            SetCardStyle(CardCredits, isDark);

            var darkComboBg = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            var darkComboBorder = new SolidColorBrush(Color.FromRgb(62, 70, 88));
            var darkComboFg = new SolidColorBrush(Color.FromRgb(243, 244, 246));
            SetComboStyle(ComboTheme, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboLanguage, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboWidgetPos, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboWidgetTextColor, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboWidgetMonitor, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboPlacement, darkComboBg, darkComboBorder, darkComboFg);

            TextHexCode.Background = darkComboBg;
            TextHexCode.BorderBrush = darkComboBorder;
            TextHexCode.Foreground = darkComboFg;

            SetSeparatorColors(isDark);

            PrivacyCallout.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            PrivacyTitle.Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
            PrivacyDesc.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            GithubButton.Background = new SolidColorBrush(Color.FromRgb(42, 47, 61));
            GithubButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 90));
            GithubButton.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
            CheckUpdatesButton.Background = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            CheckUpdatesButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 90));
            CheckUpdatesButton.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));

            BtnInstallUpdateNow.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            BtnInstallUpdateNow.Foreground = Brushes.White;

            var tabFg = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            TabBtnGeneral.Foreground = tabFg;
            TabBtnAppearance.Foreground = tabFg;
            TabBtnDetectors.Foreground = tabFg;
            TabBtnUpdates.Foreground = tabFg;
            TabBtnCredits.Foreground = tabFg;
        }
        else
        {
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 250));
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

            HeaderBorder.Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            HeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            TabHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            TabHeaderBorder.Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));

            AppHeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            AppHeaderSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            ResetDefaultsButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            ResetDefaultsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            ResetDefaultsButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

            SetCardStyle(CardGeneral, isDark);
            SetCardStyle(CardFlyout, isDark);
            SetCardStyle(CardColorPicker, isDark);
            SetCardStyle(CardDetectors, isDark);
            SetCardStyle(CardUpdates, isDark);
            SetCardStyle(CardAbout, isDark);
            SetCardStyle(CardCredits, isDark);

            var lightComboBg = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            var lightComboBorder = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            var lightComboFg = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            SetComboStyle(ComboTheme, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboLanguage, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboWidgetPos, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboWidgetTextColor, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboWidgetMonitor, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboPlacement, lightComboBg, lightComboBorder, lightComboFg);

            TextHexCode.Background = lightComboBg;
            TextHexCode.BorderBrush = lightComboBorder;
            TextHexCode.Foreground = lightComboFg;

            SetSeparatorColors(isDark);

            PrivacyCallout.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            PrivacyTitle.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            PrivacyDesc.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            GithubButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            GithubButton.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            GithubButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            CheckUpdatesButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            CheckUpdatesButton.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            CheckUpdatesButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

            BtnInstallUpdateNow.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            BtnInstallUpdateNow.Foreground = Brushes.White;

            var tabFg = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            TabBtnGeneral.Foreground = tabFg;
            TabBtnAppearance.Foreground = tabFg;
            TabBtnDetectors.Foreground = tabFg;
            TabBtnUpdates.Foreground = tabFg;
            TabBtnCredits.Foreground = tabFg;
        }
    }

    private static void SetCardStyle(Border card, bool isDark)
    {
        if (isDark)
        {
            card.Background = new SolidColorBrush(Color.FromRgb(34, 38, 47));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        }
        else
        {
            card.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        }
    }

    private static void SetComboStyle(ComboBox combo, Brush bg, Brush border, Brush fg)
    {
        combo.Background = bg;
        combo.BorderBrush = border;
        combo.Foreground = fg;

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, bg));
        itemStyle.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, fg));
        combo.ItemContainerStyle = itemStyle;
    }

    private void SetSeparatorColors(bool isDark)
    {
        var sepBrush = isDark ? new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        Sep1.Background = sepBrush;
        Sep2.Background = sepBrush;
        Sep3.Background = sepBrush;
        Sep3a.Background = sepBrush;
        Sep3b.Background = sepBrush;
        Sep3c.Background = sepBrush;
        Sep3AutoAlign.Background = sepBrush;
        Sep3Monitor.Background = sepBrush;
        Sep3d.Background = sepBrush;
        Sep3e.Background = sepBrush;
        Sep4.Background = sepBrush;
        Sep5.Background = sepBrush;
        Sep5a.Background = sepBrush;
        Sep5b.Background = sepBrush;
        SepColor.Background = sepBrush;
        Sep6.Background = sepBrush;
        Sep7.Background = sepBrush;
        Sep8.Background = sepBrush;
        Sep8b.Background = sepBrush;
        Sep9.Background = sepBrush;
        Sep10.Background = sepBrush;
        Sep11.Background = sepBrush;
        Sep12.Background = sepBrush;
        Sep13.Background = sepBrush;
        Sep13a.Background = sepBrush;
        Sep13b.Background = sepBrush;
        Sep13c.Background = sepBrush;
        SepUpdate1.Background = sepBrush;
        Sep14.Background = sepBrush;
    }

    public void ApplyLocalization()
    {
        Title = _loc.Get("Settings_Title");
        AppHeaderTitle.Text = "ClipFlyout";
        AppHeaderSubtitle.Text = _loc.Get("Settings_SubTitle");
        ResetDefaultsButton.Content = _loc.Get("About_Reset");

        // Tab Headers
        TabBtnGeneral.Content = _loc.Get("Tab_General");
        TabBtnAppearance.Content = _loc.Get("Tab_Appearance");
        TabBtnDetectors.Content = _loc.Get("Tab_Detectors");
        TabBtnUpdates.Content = _loc.Get("Tab_Updates");
        TabBtnCredits.Content = _loc.Get("Tab_Credits");

        SecGeneralTitle.Text = _loc.Get("Section_General");
        LblMonitoring.Text = _loc.Get("Setting_Monitoring");
        DescMonitoring.Text = _loc.Get("Setting_Monitoring_Desc");
        LblShowWidget.Text = _loc.Get("Setting_ShowTaskbarWidget");
        DescShowWidget.Text = _loc.Get("Setting_ShowTaskbarWidget_Desc");
        LblWidgetPos.Text = _loc.Get("Setting_WidgetPosition");
        DescWidgetPos.Text = _loc.Get("Setting_WidgetPosition_Desc");
        LblWidgetAutoAlign.Text = _loc.Get("Setting_WidgetAutoAlign");
        DescWidgetAutoAlign.Text = _loc.Get("Setting_WidgetAutoAlign_Desc");
        LblWidgetMonitor.Text = _loc.Get("Setting_WidgetMonitor");
        DescWidgetMonitor.Text = _loc.Get("Setting_WidgetMonitor_Desc");
        LblWidgetOffset.Text = _loc.Get("Setting_WidgetOffsetX");
        DescWidgetOffset.Text = _loc.Get("Setting_WidgetOffsetX_Desc");
        LblWidgetTextColor.Text = _loc.Get("Setting_WidgetTextColor");
        DescWidgetTextColor.Text = _loc.Get("Setting_WidgetTextColor_Desc");
        LblIgnorePasswords.Text = _loc.Get("Setting_IgnorePasswordManagers");
        DescIgnorePasswords.Text = _loc.Get("Setting_IgnorePasswordManagers_Desc");
        LblRecallHotkey.Text = _loc.Get("Setting_EnableRecallHotkey");
        DescRecallHotkey.Text = _loc.Get("Setting_EnableRecallHotkey_Desc");
        LblStartup.Text = _loc.Get("Setting_Startup");
        DescStartup.Text = _loc.Get("Setting_Startup_Desc");
        LblLang.Text = _loc.Get("Setting_Language");
        DescLang.Text = _loc.Get("Setting_Language_Desc");

        SecFlyoutTitle.Text = _loc.Get("Section_Flyout");
        LblTheme.Text = _loc.Get("Setting_Theme");
        DescTheme.Text = _loc.Get("Setting_Theme_Desc");
        LblPlacement.Text = _loc.Get("Setting_Placement");
        DescPlacement.Text = _loc.Get("Setting_Placement_Desc");
        LblOpacity.Text = _loc.Get("Setting_Opacity");
        DescOpacity.Text = _loc.Get("Setting_Opacity_Desc");
        LblDuration.Text = _loc.Get("Setting_Duration");
        DescDuration.Text = _loc.Get("Setting_Duration_Desc");
        LblHoverDuration.Text = _loc.Get("Setting_HoverDuration");
        DescHoverDuration.Text = _loc.Get("Setting_HoverDuration_Desc");

        SecColorPickerTitle.Text = _loc.Get("Setting_AccentColor");
        LblColorPreset.Text = _loc.Get("Color_Preset");
        LblColorRed.Text = _loc.Get("Color_Red");
        LblColorGreen.Text = _loc.Get("Color_Green");
        LblColorBlue.Text = _loc.Get("Color_Blue");

        SecDetectorsTitle.Text = _loc.Get("Section_Detectors");
        SecDetectorsSubtitle.Text = _loc.Get("Section_Detectors_Desc");
        LblDetHex.Text = _loc.Get("Detector_HexColor");
        DescDetHex.Text = _loc.Get("Detector_HexColor_Desc");
        LblDetTimestamp.Text = _loc.Get("Detector_Timestamp");
        DescDetTimestamp.Text = _loc.Get("Detector_Timestamp_Desc");
        LblDetJson.Text = _loc.Get("Detector_Json");
        DescDetJson.Text = _loc.Get("Detector_Json_Desc");
        LblDetUrl.Text = _loc.Get("Detector_Url");
        DescDetUrl.Text = _loc.Get("Detector_Url_Desc");
        LblDetEmail.Text = _loc.Get("Detector_Email");
        DescDetEmail.Text = _loc.Get("Detector_Email_Desc");
        LblDetBase64.Text = _loc.Get("Detector_Base64");
        DescDetBase64.Text = _loc.Get("Detector_Base64_Desc");
        LblDetTable.Text = _loc.Get("Detector_Table");
        DescDetTable.Text = _loc.Get("Detector_Table_Desc");
        LblDetCode.Text = _loc.Get("Detector_Code");
        DescDetCode.Text = _loc.Get("Detector_Code_Desc");
        LblDetImage.Text = _loc.Get("Detector_Image");
        DescDetImage.Text = _loc.Get("Detector_Image_Desc");
        LblDetText.Text = _loc.Get("Detector_PlainText");
        DescDetText.Text = _loc.Get("Detector_PlainText_Desc");
        LblCleanUrl.Text = _loc.Get("Detector_CleanUrl");
        DescCleanUrl.Text = _loc.Get("Detector_CleanUrl_Desc");
        LblCaseConverter.Text = _loc.Get("Detector_CaseConverter");
        DescCaseConverter.Text = _loc.Get("Detector_CaseConverter_Desc");
        LblJwt.Text = _loc.Get("Detector_Jwt");
        DescJwt.Text = _loc.Get("Detector_Jwt_Desc");

        SecUpdatesTitle.Text = _loc.Get("Tab_Updates");
        LblAutoUpdate.Text = _loc.Get("Setting_AutoUpdate");
        DescAutoUpdate.Text = _loc.Get("Setting_AutoUpdate_Desc");
        CheckUpdatesButton.Content = _loc.Get("Update_CheckNow");
        BtnInstallUpdateNow.Content = _loc.Get("Update_InstallNow");

        SecAboutTitle.Text = _loc.Get("Section_About");
        PrivacyTitle.Text = _loc.Get("About_Privacy_Title");
        PrivacyDesc.Text = _loc.Get("About_Privacy_Desc");
        AboutVersion.Text = _loc.Get("About_Version", AppInfo.DisplayVersion);

        SecCreditsTitle.Text = _loc.Get("Section_Credits");
        CreditsProject.Text = _loc.Get("Credits_Project");
        CreditsAuthor.Text = _loc.Get("Credits_Author");
        CreditsLicense.Text = _loc.Get("Credits_License");
        CreditsNotice.Text = _loc.Get("Credits_Desc");

        // Refresh dropdown display texts
        int themeIdx = ComboTheme.SelectedIndex;
        int langIdx = ComboLanguage.SelectedIndex;
        int widgetPosIdx = ComboWidgetPos.SelectedIndex;
        int widgetTextColorIdx = ComboWidgetTextColor.SelectedIndex;
        int widgetMonitorIdx = ComboWidgetMonitor.SelectedIndex;
        int placementIdx = ComboPlacement.SelectedIndex;

        PopulateDropdowns();

        ComboTheme.SelectedIndex = themeIdx;
        ComboLanguage.SelectedIndex = langIdx;
        ComboWidgetPos.SelectedIndex = widgetPosIdx;
        ComboWidgetTextColor.SelectedIndex = widgetTextColorIdx;
        ComboWidgetMonitor.SelectedIndex = widgetMonitorIdx;
        ComboPlacement.SelectedIndex = placementIdx;
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc.Get("About_Reset_Confirm"),
            "ClipFlyout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            var defaultSettings = new AppSettings();
            _settings.SaveSettings(defaultSettings);
            _theme.Mode = defaultSettings.Theme;
            _loc.CurrentLanguage = defaultSettings.Language;

            _isInitializing = true;
            LoadSettingsValues();
            _isInitializing = false;
        }
    }

    private void GithubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/featlis/ClipFlyout",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        BtnInstallUpdateNow.Visibility = Visibility.Collapsed;
        TextUpdateStatus.Text = _loc.Get("Update_Checking");
        TextUpdateStatus.Foreground = _theme.IsDarkTheme
            ? new SolidColorBrush(Color.FromRgb(156, 163, 175))
            : new SolidColorBrush(Color.FromRgb(100, 116, 139));

        try
        {
            var update = await UpdateService.Instance.CheckForUpdateAsync();
            if (update is null)
            {
                TextUpdateStatus.Text = $"✓ {_loc.Get("Update_UpToDate")}";
                TextUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                return;
            }

            _pendingUpdate = update;
            TextUpdateStatus.Text = $"★ v{update.Version}";
            TextUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            BtnInstallUpdateNow.Visibility = Visibility.Visible;
        }
        catch
        {
            TextUpdateStatus.Text = $"⚠ {_loc.Get("Update_Failed")}";
            TextUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void BtnInstallUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        BtnInstallUpdateNow.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;
        TextUpdateStatus.Text = _loc.Get("Update_Downloading");
        TextUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246));

        try
        {
            await UpdateService.Instance.DownloadAndStartInstallerAsync(_pendingUpdate);
            Application.Current.Shutdown();
        }
        catch
        {
            TextUpdateStatus.Text = $"⚠ {_loc.Get("Update_Failed")}";
            TextUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            BtnInstallUpdateNow.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
