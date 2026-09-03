using System;
using System.Diagnostics;
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
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private IntPtr _hwnd = IntPtr.Zero;

    public SettingsWindow()
    {
        InitializeComponent();

        SourceInitialized += SettingsWindow_SourceInitialized;
        _loc.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);
        _theme.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwnd = helper.Handle;
        ApplyMicaEffect();
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

        // Placement dropdown (including TopLeft)
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

        SelectComboByTag(ComboTheme, cfg.Theme);
        SelectComboByTag(ComboLanguage, cfg.Language);
        SelectComboByTag(ComboPlacement, cfg.Placement);

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
        ToggleDetBase64.IsOn = cfg.DetectBase64;
        ToggleDetTable.IsOn = cfg.DetectTable;
        ToggleDetCode.IsOn = cfg.DetectCode;
        ToggleDetImage.IsOn = cfg.DetectImage;
        ToggleDetText.IsOn = cfg.DetectPlainText;
    }

    private void HookToggleEvents()
    {
        ToggleMonitoring.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.IsMonitoringEnabled = val); };
        ToggleStartup.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.LaunchOnStartup = val); };

        ToggleDetHex.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectHexColor = val); };
        ToggleDetTimestamp.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectTimestamp = val); };
        ToggleDetJson.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectJson = val); };
        ToggleDetUrl.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectUrl = val); };
        ToggleDetBase64.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectBase64 = val); };
        ToggleDetTable.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectTable = val); };
        ToggleDetCode.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectCode = val); };
        ToggleDetImage.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectImage = val); };
        ToggleDetText.Toggled += (_, val) => { if (!_isInitializing) _settings.UpdateSettings(s => s.DetectPlainText = val); };
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
            AppHeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            AppHeaderSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            ResetDefaultsButton.Background = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            ResetDefaultsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 88));
            ResetDefaultsButton.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));

            SetCardStyle(CardGeneral, isDark);
            SetCardStyle(CardFlyout, isDark);
            SetCardStyle(CardDetectors, isDark);
            SetCardStyle(CardAbout, isDark);

            var darkComboBg = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            var darkComboBorder = new SolidColorBrush(Color.FromRgb(62, 70, 88));
            var darkComboFg = new SolidColorBrush(Color.FromRgb(243, 244, 246));
            SetComboStyle(ComboTheme, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboLanguage, darkComboBg, darkComboBorder, darkComboFg);
            SetComboStyle(ComboPlacement, darkComboBg, darkComboBorder, darkComboFg);

            SetSeparatorColors(isDark);

            PrivacyCallout.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            PrivacyTitle.Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
            PrivacyDesc.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            GithubButton.Background = new SolidColorBrush(Color.FromRgb(42, 47, 61));
            GithubButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 90));
            GithubButton.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
        }
        else
        {
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 250));
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

            HeaderBorder.Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            HeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            AppHeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            AppHeaderSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            ResetDefaultsButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            ResetDefaultsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            ResetDefaultsButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

            SetCardStyle(CardGeneral, isDark);
            SetCardStyle(CardFlyout, isDark);
            SetCardStyle(CardDetectors, isDark);
            SetCardStyle(CardAbout, isDark);

            var lightComboBg = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            var lightComboBorder = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            var lightComboFg = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            SetComboStyle(ComboTheme, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboLanguage, lightComboBg, lightComboBorder, lightComboFg);
            SetComboStyle(ComboPlacement, lightComboBg, lightComboBorder, lightComboFg);

            SetSeparatorColors(isDark);

            PrivacyCallout.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            PrivacyTitle.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            PrivacyDesc.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            GithubButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            GithubButton.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            GithubButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
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
    }

    private void SetSeparatorColors(bool isDark)
    {
        var sepBrush = isDark ? new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        Sep1.Background = sepBrush;
        Sep2.Background = sepBrush;
        Sep3.Background = sepBrush;
        Sep4.Background = sepBrush;
        Sep5.Background = sepBrush;
        Sep5b.Background = sepBrush;
        Sep6.Background = sepBrush;
        Sep7.Background = sepBrush;
        Sep8.Background = sepBrush;
        Sep9.Background = sepBrush;
        Sep10.Background = sepBrush;
        Sep11.Background = sepBrush;
        Sep12.Background = sepBrush;
        Sep13.Background = sepBrush;
    }

    public void ApplyLocalization()
    {
        Title = _loc.Get("Settings_Title");
        AppHeaderTitle.Text = "ClipFlyout";
        AppHeaderSubtitle.Text = _loc.Get("Settings_SubTitle");
        ResetDefaultsButton.Content = _loc.Get("About_Reset");

        SecGeneralTitle.Text = _loc.Get("Section_General");
        LblMonitoring.Text = _loc.Get("Setting_Monitoring");
        DescMonitoring.Text = _loc.Get("Setting_Monitoring_Desc");
        LblStartup.Text = _loc.Get("Setting_Startup");
        DescStartup.Text = _loc.Get("Setting_Startup_Desc");
        LblTheme.Text = _loc.Get("Setting_Theme");
        DescTheme.Text = _loc.Get("Setting_Theme_Desc");
        LblLang.Text = _loc.Get("Setting_Language");
        DescLang.Text = _loc.Get("Setting_Language_Desc");

        SecFlyoutTitle.Text = _loc.Get("Section_Flyout");
        LblPlacement.Text = _loc.Get("Setting_Placement");
        DescPlacement.Text = _loc.Get("Setting_Placement_Desc");
        LblOpacity.Text = _loc.Get("Setting_Opacity");
        DescOpacity.Text = _loc.Get("Setting_Opacity_Desc");
        LblDuration.Text = _loc.Get("Setting_Duration");
        DescDuration.Text = _loc.Get("Setting_Duration_Desc");
        LblHoverDuration.Text = _loc.Get("Setting_HoverDuration");
        DescHoverDuration.Text = _loc.Get("Setting_HoverDuration_Desc");

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

        SecAboutTitle.Text = _loc.Get("Section_About");
        PrivacyTitle.Text = _loc.Get("About_Privacy_Title");
        PrivacyDesc.Text = _loc.Get("About_Privacy_Desc");
        AboutVersion.Text = _loc.Get("About_Version");

        // Refresh dropdown display texts
        int themeIdx = ComboTheme.SelectedIndex;
        int langIdx = ComboLanguage.SelectedIndex;
        int placementIdx = ComboPlacement.SelectedIndex;

        PopulateDropdowns();

        ComboTheme.SelectedIndex = themeIdx;
        ComboLanguage.SelectedIndex = langIdx;
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
}
