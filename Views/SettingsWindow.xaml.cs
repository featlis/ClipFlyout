using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipFlyout.Controls;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly DispatcherTimer _sliderDebounceTimer;
    private bool _isInitializing = true;
    private bool _isSyncingColor;
    private bool _isSaving;
    private bool _isRecordingHotkey;
    private string? _editingSnippetId;
    private IntPtr _hwnd = IntPtr.Zero;
    private UpdateRelease? _pendingUpdate;
    private bool _isCheckingUpdates;

    private readonly Action _themeChangedHandler;
    private readonly Action _languageChangedHandler;
    private readonly Action _historyChangedHandler;
    private readonly Action _snippetsChangedHandler;
    private readonly Action<AppSettings> _settingsChangedHandler;
    private readonly Func<bool>? _hotkeyFailedProvider;

    public SettingsWindow(Func<bool>? hotkeyFailedProvider = null)
    {
        _hotkeyFailedProvider = hotkeyFailedProvider;
        InitializeComponent();

        try
        {
            var iconSrc = AppIconHelper.GetAppIconBitmapSource(64);
            Icon = iconSrc;
            HeaderAppIcon.Source = iconSrc;
            AboutAppIcon.Source = iconSrc;
        }
        catch { }

        NavVersionText.Text = AppInfo.DisplayVersion;

        // 150ms debounce so dragging sliders never thrashes disk I/O or DWM
        _sliderDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _sliderDebounceTimer.Tick += (_, _) =>
        {
            _sliderDebounceTimer.Stop();
            SaveAllSettings();
        };

        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(ApplyWindowBackdrop);
        };
        _languageChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(ApplyLocalization);
        };
        _historyChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(UpdateHistoryCountLabel);
        };
        _snippetsChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(RefreshSnippetsList);
        };
        _settingsChangedHandler = _ =>
        {
            if (!_isSaving && !Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_isSaving)
                    {
                        LoadValuesFromSettings();
                        UpdateHotkeyConflictBanner();
                    }
                });
            }
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += SettingsWindow_Loaded;
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        Closing += (_, _) =>
        {
            _isRecordingHotkey = false;
            if (_sliderDebounceTimer.IsEnabled)
            {
                _sliderDebounceTimer.Stop();
                SaveAllSettings();
            }
        };
        Closed += (_, _) =>
        {
            _sliderDebounceTimer.Stop();
            ThemeService.Instance.ThemeChanged -= _themeChangedHandler;
            _loc.LanguageChanged -= _languageChangedHandler;
            HistoryService.Instance.HistoryChanged -= _historyChangedHandler;
            SnippetService.Instance.SnippetsChanged -= _snippetsChangedHandler;
            _settings.SettingsChanged -= _settingsChangedHandler;
        };

        ThemeService.Instance.ThemeChanged += _themeChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        HistoryService.Instance.HistoryChanged += _historyChangedHandler;
        SnippetService.Instance.SnippetsChanged += _snippetsChangedHandler;
        _settings.SettingsChanged += _settingsChangedHandler;

        // Hydrate localization & values upfront so offscreen/first-frame renders look crisp immediately
        ApplyLocalization();
        LoadValuesFromSettings();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        ApplyWindowBackdrop();
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        UpdateHotkeyConflictBanner();
        UpdateHistoryCountLabel();
    }

    private void ApplyWindowBackdrop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32.EnableMica(_hwnd, ThemeService.Instance.IsDarkTheme);
        }
    }

    public void SelectSnippetsTab()
    {
        if (NavSnippets != null)
        {
            NavSnippets.IsChecked = true;
        }
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && PageGeneral == null) return;
        if (sender is not RadioButton rb || rb.Tag is not string tagStr) return;

        int page = int.TryParse(tagStr, out int p) ? p : 0;
        PageGeneral.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageFlyout.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageWidget.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageDetectors.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == 5 ? Visibility.Visible : Visibility.Collapsed;
        PageSnippets.Visibility = page == 6 ? Visibility.Visible : Visibility.Collapsed;

        PageHeaderTitle.Text = page switch
        {
            0 => _loc.Get("Nav_General"),
            1 => _loc.Get("Nav_Flyout"),
            2 => _loc.Get("Nav_Widget"),
            3 => _loc.Get("Nav_Detectors"),
            4 => _loc.Get("Nav_History"),
            6 => _loc.Get("Nav_Snippets"),
            _ => _loc.Get("Nav_About")
        };

        if (page == 4) UpdateHistoryCountLabel();
        if (page == 6) RefreshSnippetsList();
    }

    private void ApplyLocalization()
    {
        bool prevInit = _isInitializing;
        _isInitializing = true;
        try
        {
            Title = _loc.Get("Settings_Title");

            // Navigation items
            NavGeneralText.Text = _loc.Get("Nav_General");
            NavFlyoutText.Text = _loc.Get("Nav_Flyout");
            NavWidgetText.Text = _loc.Get("Nav_Widget");
            NavDetectorsText.Text = _loc.Get("Nav_Detectors");
            NavHistoryText.Text = _loc.Get("Nav_History");
            NavSnippetsText.Text = _loc.Get("Nav_Snippets");
            NavAboutText.Text = _loc.Get("Nav_About");

            // Update active page header
            if (NavGeneral.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_General");
            else if (NavFlyout.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_Flyout");
            else if (NavWidget.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_Widget");
            else if (NavDetectors.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_Detectors");
            else if (NavHistory.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_History");
            else if (NavSnippets.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_Snippets");
            else if (NavAbout.IsChecked == true) PageHeaderTitle.Text = _loc.Get("Nav_About");

            // Page 0: General
            CardMonitoring.Header = _loc.Get("Setting_Monitoring");
            CardMonitoring.Description = _loc.Get("Setting_Monitoring_Desc");
            CardStartup.Header = _loc.Get("Setting_Startup");
            CardStartup.Description = _loc.Get("Setting_Startup_Desc");
            UpdateHotkeyLabels();
            CardRecallHotkey.Description = _loc.Get("Setting_EnableRecallHotkey_Desc");
            BtnResetHotkey.Content = _loc.Get("Hotkey_Reset");
            CardLanguage.Header = _loc.Get("Setting_Language");
            CardLanguage.Description = _loc.Get("Setting_Language_Desc");

            // Page 1: Flyout & Appearance
            CardTheme.Header = _loc.Get("Setting_Theme");
            CardTheme.Description = _loc.Get("Setting_Theme_Desc");
            CardSystemAccent.Header = _loc.Get("Setting_UseSystemAccent");
            CardSystemAccent.Description = _loc.Get("Setting_UseSystemAccent_Desc");
            LabelAccentColor.Text = _loc.Get("Setting_AccentColor");
            DescAccentColor.Text = _loc.Get("Setting_AccentColor_Desc");
            LabelColorR.Text = _loc.Get("Color_Red");
            LabelColorG.Text = _loc.Get("Color_Green");
            LabelColorB.Text = _loc.Get("Color_Blue");
            LabelColorHex.Text = _loc.Get("Color_Hex");
            SwatchBlue.ToolTip = _loc.Get("Accent_Blue");
            SwatchTeal.ToolTip = _loc.Get("Accent_Teal");
            SwatchGreen.ToolTip = _loc.Get("Accent_Green");
            SwatchPurple.ToolTip = _loc.Get("Accent_Purple");
            SwatchPink.ToolTip = _loc.Get("Accent_Pink");
            SwatchOrange.ToolTip = _loc.Get("Accent_Orange");
            CardPlacement.Header = _loc.Get("Setting_Placement");
            CardPlacement.Description = _loc.Get("Setting_Placement_Desc");
            CardOpacity.Header = _loc.Get("Setting_Opacity");
            CardOpacity.Description = _loc.Get("Setting_Opacity_Desc");
            CardDuration.Header = _loc.Get("Setting_Duration");
            CardDuration.Description = _loc.Get("Setting_Duration_Desc");
            CardHoverDuration.Header = _loc.Get("Setting_HoverDuration");
            CardHoverDuration.Description = _loc.Get("Setting_HoverDuration_Desc");
            CardSuppressFullscreen.Header = _loc.Get("Setting_SuppressFullscreen");
            CardSuppressFullscreen.Description = _loc.Get("Setting_SuppressFullscreen_Desc");

            // Page 2: Taskbar Widget
            CardShowWidget.Header = _loc.Get("Setting_ShowTaskbarWidget");
            CardShowWidget.Description = _loc.Get("Setting_ShowTaskbarWidget_Desc");
            CardWidgetPosition.Header = _loc.Get("Setting_WidgetPosition");
            CardWidgetPosition.Description = _loc.Get("Setting_WidgetPosition_Desc");
            CardWidgetAutoAlign.Header = _loc.Get("Setting_WidgetAutoAlign");
            CardWidgetAutoAlign.Description = _loc.Get("Setting_WidgetAutoAlign_Desc");
            CardWidgetMonitor.Header = _loc.Get("Setting_WidgetMonitor");
            CardWidgetMonitor.Description = _loc.Get("Setting_WidgetMonitor_Desc");
            CardWidgetTextColor.Header = _loc.Get("Setting_WidgetTextColor");
            CardWidgetTextColor.Description = _loc.Get("Setting_WidgetTextColor_Desc");
            CardWidgetOffset.Header = _loc.Get("Setting_WidgetOffsetX");
            CardWidgetOffset.Description = _loc.Get("Setting_WidgetOffsetX_Desc");

            // Page 3: Detectors
            GroupDetectorsCore.Text = _loc.Get("Section_Detectors_Core");
            GroupDetectorsTools.Text = _loc.Get("Section_Detectors_Tools");
            CardDetectHex.Header = _loc.Get("Detector_HexColor");
            CardDetectHex.Description = _loc.Get("Detector_HexColor_Desc");
            CardDetectUrl.Header = _loc.Get("Detector_Url");
            CardDetectUrl.Description = _loc.Get("Detector_Url_Desc");
            CardDetectFileDrop.Header = _loc.Get("Detector_FileDrop");
            CardDetectFileDrop.Description = _loc.Get("Detector_FileDrop_Desc");
            CardDetectImage.Header = _loc.Get("Detector_Image");
            CardDetectImage.Description = _loc.Get("Detector_Image_Desc");
            CardDetectJson.Header = _loc.Get("Detector_Json");
            CardDetectJson.Description = _loc.Get("Detector_Json_Desc");
            CardDetectEmail.Header = _loc.Get("Detector_Email");
            CardDetectEmail.Description = _loc.Get("Detector_Email_Desc");
            CardDetectTimestamp.Header = _loc.Get("Detector_Timestamp");
            CardDetectTimestamp.Description = _loc.Get("Detector_Timestamp_Desc");
            CardDetectIsoDate.Header = _loc.Get("Detector_IsoDateTime");
            CardDetectIsoDate.Description = _loc.Get("Detector_IsoDateTime_Desc");
            CardDetectBase64.Header = _loc.Get("Detector_Base64");
            CardDetectBase64.Description = _loc.Get("Detector_Base64_Desc");
            CardDetectTable.Header = _loc.Get("Detector_Table");
            CardDetectTable.Description = _loc.Get("Detector_Table_Desc");
            CardDetectCode.Header = _loc.Get("Detector_Code");
            CardDetectCode.Description = _loc.Get("Detector_Code_Desc");
            CardDetectPlainText.Header = _loc.Get("Detector_PlainText");
            CardDetectPlainText.Description = _loc.Get("Detector_PlainText_Desc");
            CardCleanUrl.Header = _loc.Get("Detector_CleanUrl");
            CardCleanUrl.Description = _loc.Get("Detector_CleanUrl_Desc");
            CardCaseConverter.Header = _loc.Get("Detector_CaseConverter");
            CardCaseConverter.Description = _loc.Get("Detector_CaseConverter_Desc");
            CardJwt.Header = _loc.Get("Detector_Jwt");
            CardJwt.Description = _loc.Get("Detector_Jwt_Desc");
            CardEnableOcr.Header = _loc.Get("Detector_Ocr");
            CardEnableOcr.Description = _loc.Get("Detector_Ocr_Desc");
            CardDetectFilePath.Header = _loc.Get("Detector_FilePath");
            CardDetectFilePath.Description = _loc.Get("Detector_FilePath_Desc");
            CardDetectMath.Header = _loc.Get("Detector_Math");
            CardDetectMath.Description = _loc.Get("Detector_Math_Desc");
            CardDetectUuid.Header = _loc.Get("Detector_Uuid");
            CardDetectUuid.Description = _loc.Get("Detector_Uuid_Desc");

            // Page 4: History & Privacy
            CardIgnorePasswordManagers.Header = _loc.Get("Setting_IgnorePasswordManagers");
            CardIgnorePasswordManagers.Description = _loc.Get("Setting_IgnorePasswordManagers_Desc");
            CardExcludedApps.Header = _loc.Get("Setting_ExcludedApps");
            CardExcludedApps.Description = _loc.Get("Setting_ExcludedApps_Desc");
            CardPersistHistory.Header = _loc.Get("Setting_PersistHistory");
            CardPersistHistory.Description = _loc.Get("Setting_PersistHistory_Desc");
            CardClearHistory.Header = _loc.Get("Setting_ClearHistory");
            BtnClearHistory.Content = _loc.Get("Setting_ClearHistory_Btn");
            CardLocalProcessing.Header = _loc.Get("About_Privacy_Title");
            CardLocalProcessing.Description = _loc.Get("About_Privacy_Desc");
            UpdateHistoryCountLabel();

            // Page 6: Snippets
            LblSnippetEditorHeader.Text = _loc.Get("Snippets_Editor_Header");
            TxtSnippetTitlePlaceholder.Text = _loc.Get("Snippets_Title_Placeholder");
            TxtSnippetContentPlaceholder.Text = _loc.Get("Snippets_Content_Placeholder");
            BtnSaveSnippet.Content = string.IsNullOrEmpty(_editingSnippetId)
                ? _loc.Get("Snippets_Save")
                : _loc.Get("Snippets_Update");
            BtnCancelSnippetEdit.Content = _loc.Get("Snippets_CancelEdit");
            TxtNoSnippetsSettings.Text = _loc.Get("Snippets_Empty");
            RefreshSnippetsList();

            // Page 5: Updates & About
            GroupUpdatesHeader.Text = _loc.Get("Section_Updates");
            GroupAboutHeader.Text = _loc.Get("Section_About");
            CardAutoUpdate.Header = _loc.Get("Setting_AutoUpdate");
            CardAutoUpdate.Description = _loc.Get("Setting_AutoUpdate_Desc");
            CardManualUpdate.Header = _loc.Get("Setting_ManualUpdate");
            CardManualUpdate.Description = _loc.Get("Setting_ManualUpdate_Desc");
            CheckUpdatesButton.Content = _loc.Get("Update_CheckNow");
            BtnInstallUpdateNow.Content = _loc.Get("Update_InstallNow");
            AboutVersionText.Text = _loc.Get("About_Version", AppInfo.DisplayVersion);
            AboutRuntimeText.Text = _loc.Get("About_Runtime");
            AboutAuthorText.Text = _loc.Get("Credits_Author");
            AboutLicenseText.Text = _loc.Get("Credits_License");
            CardGithub.Header = _loc.Get("About_Github");
            CardGithub.Description = _loc.Get("About_Github_Desc");
            GithubButton.Content = _loc.Get("About_Open");
            CardReset.Header = _loc.Get("About_Reset");
            CardReset.Description = _loc.Get("About_Reset_Desc");
            ResetDefaultsButton.Content = _loc.Get("About_Reset");

            PopulateDropdowns();
        }
        finally
        {
            _isInitializing = prevInit;
        }
    }

    private void PopulateDropdowns()
    {
        var cfg = _settings.Current;

        CmbTheme.Items.Clear();
        CmbTheme.Items.Add(_loc.Get("Theme_System"));
        CmbTheme.Items.Add(_loc.Get("Theme_Light"));
        CmbTheme.Items.Add(_loc.Get("Theme_Dark"));
        CmbTheme.SelectedIndex = (int)cfg.Theme;

        CmbLanguage.Items.Clear();
        CmbLanguage.Items.Add(_loc.Get("Lang_Auto"));
        CmbLanguage.Items.Add(_loc.Get("Lang_Ja"));
        CmbLanguage.Items.Add(_loc.Get("Lang_En"));
        CmbLanguage.SelectedIndex = (int)cfg.Language;

        CmbPlacement.Items.Clear();
        CmbPlacement.Items.Add(_loc.Get("Placement_BottomRight"));
        CmbPlacement.Items.Add(_loc.Get("Placement_TopRight"));
        CmbPlacement.Items.Add(_loc.Get("Placement_TopLeft"));
        CmbPlacement.Items.Add(_loc.Get("Placement_BottomLeft"));
        CmbPlacement.Items.Add(_loc.Get("Placement_NearCursor"));
        CmbPlacement.SelectedIndex = (int)cfg.Placement;

        CmbWidgetPosition.Items.Clear();
        CmbWidgetPosition.Items.Add(_loc.Get("Widget_Pos_TrayLeft"));
        CmbWidgetPosition.Items.Add(_loc.Get("Widget_Pos_CenterRight"));
        CmbWidgetPosition.Items.Add(_loc.Get("Widget_Pos_CenterLeft"));
        CmbWidgetPosition.Items.Add(_loc.Get("Widget_Pos_FarLeft"));
        CmbWidgetPosition.Items.Add(_loc.Get("Widget_Pos_AboveTaskbar"));
        CmbWidgetPosition.SelectedIndex = (int)cfg.WidgetPosition;

        CmbWidgetMonitor.Items.Clear();
        CmbWidgetMonitor.Items.Add(_loc.Get("Widget_Monitor_Primary"));
        CmbWidgetMonitor.Items.Add(_loc.Get("Widget_Monitor_Cursor"));
        CmbWidgetMonitor.Items.Add(_loc.Get("Widget_Monitor_Display1"));
        CmbWidgetMonitor.Items.Add(_loc.Get("Widget_Monitor_Display2"));
        CmbWidgetMonitor.Items.Add(_loc.Get("Widget_Monitor_Display3"));
        CmbWidgetMonitor.SelectedIndex = (int)cfg.WidgetMonitor;

        CmbWidgetTextColor.Items.Clear();
        CmbWidgetTextColor.Items.Add(_loc.Get("Widget_TextColor_Auto"));
        CmbWidgetTextColor.Items.Add(_loc.Get("Widget_TextColor_Light"));
        CmbWidgetTextColor.Items.Add(_loc.Get("Widget_TextColor_Dark"));
        CmbWidgetTextColor.SelectedIndex = (int)cfg.WidgetTextColor;
    }

    private void LoadValuesFromSettings()
    {
        _isInitializing = true;
        try
        {
            var cfg = _settings.Current;

            ToggleMonitoring.IsOn = cfg.IsMonitoringEnabled;
            ToggleStartup.IsOn = cfg.LaunchOnStartup;
            ToggleRecallHotkey.IsOn = cfg.EnableRecallHotkey;
            UpdateHotkeyLabels();
            CmbLanguage.SelectedIndex = (int)cfg.Language;

            CmbTheme.SelectedIndex = (int)cfg.Theme;
            ToggleSystemAccent.IsOn = cfg.UseSystemAccentColor;
            SyncAccentControls(cfg.AccentColor);
            UpdateAccentPickerEnabledState();

            CmbPlacement.SelectedIndex = (int)cfg.Placement;
            SliderOpacity.Value = cfg.OpacityPercent;
            TxtOpacityVal.Text = $"{cfg.OpacityPercent:F0}%";
            SliderDuration.Value = cfg.DisplayDurationSeconds;
            TxtDurationVal.Text = $"{cfg.DisplayDurationSeconds:F1}s";
            SliderHoverDuration.Value = cfg.HoverLeaveDurationSeconds;
            TxtHoverDurationVal.Text = $"{cfg.HoverLeaveDurationSeconds:F1}s";
            ToggleSuppressFullscreen.IsOn = cfg.SuppressInFullscreen;

            ToggleShowWidget.IsOn = cfg.ShowTaskbarWidget;
            CmbWidgetPosition.SelectedIndex = (int)cfg.WidgetPosition;
            ToggleWidgetAutoAlign.IsOn = cfg.WidgetAutoAlign;
            CmbWidgetMonitor.SelectedIndex = (int)cfg.WidgetMonitor;
            CmbWidgetTextColor.SelectedIndex = (int)cfg.WidgetTextColor;
            SliderWidgetOffset.Value = cfg.WidgetOffsetX;
            TxtWidgetOffsetVal.Text = $"{cfg.WidgetOffsetX:F0}px";
            UpdateWidgetSubSettingsState();

            ToggleDetectHex.IsOn = cfg.DetectHexColor;
            ToggleDetectUrl.IsOn = cfg.DetectUrl;
            ToggleDetectFileDrop.IsOn = cfg.DetectFileDrop;
            ToggleDetectImage.IsOn = cfg.DetectImage;
            ToggleDetectJson.IsOn = cfg.DetectJson;
            ToggleDetectEmail.IsOn = cfg.DetectEmail;
            ToggleDetectTimestamp.IsOn = cfg.DetectTimestamp;
            ToggleDetectIsoDate.IsOn = cfg.DetectIsoDateTime;
            ToggleDetectBase64.IsOn = cfg.DetectBase64;
            ToggleDetectTable.IsOn = cfg.DetectTable;
            ToggleDetectCode.IsOn = cfg.DetectCode;
            ToggleDetectPlainText.IsOn = cfg.DetectPlainText;
            ToggleCleanUrl.IsOn = cfg.EnableCleanUrl;
            ToggleCaseConverter.IsOn = cfg.EnableCaseConverter;
            ToggleJwt.IsOn = cfg.EnableJwtDetector;
            ToggleEnableOcr.IsOn = cfg.EnableOcr;
            ToggleDetectFilePath.IsOn = cfg.DetectFilePath;
            ToggleDetectMath.IsOn = cfg.DetectMath;
            ToggleDetectUuid.IsOn = cfg.DetectUuid;

            ToggleIgnorePasswordManagers.IsOn = cfg.IgnorePasswordManagers;
            TxtExcludedApps.Text = cfg.ExcludedApps;
            TogglePersistHistory.IsOn = cfg.PersistHistory;
            ToggleAutoUpdate.IsOn = cfg.AutomaticallyInstallUpdates;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void SyncAccentControls(string hex)
    {
        _isSyncingColor = true;
        try
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { c = Color.FromRgb(0, 120, 212); }

            SliderColorR.Value = c.R;
            SliderColorG.Value = c.G;
            SliderColorB.Value = c.B;
            ValColorR.Text = c.R.ToString(CultureInfo.InvariantCulture);
            ValColorG.Text = c.G.ToString(CultureInfo.InvariantCulture);
            ValColorB.Text = c.B.ToString(CultureInfo.InvariantCulture);
            string upperHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            TextHexCode.Text = upperHex;
            CurrentAccentPreview.Background = ThemeService.CreateFrozenBrush(c);
            UpdateSelectedSwatchHighlight(upperHex);
        }
        finally
        {
            _isSyncingColor = false;
        }
    }

    private void UpdateSelectedSwatchHighlight(string hex)
    {
        Button[] swatches = [SwatchBlue, SwatchTeal, SwatchGreen, SwatchPurple, SwatchPink, SwatchOrange];
        foreach (var btn in swatches)
        {
            if (btn.Background is SolidColorBrush scb)
            {
                string swatchHex = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
                btn.Tag = swatchHex.Equals(hex, StringComparison.OrdinalIgnoreCase) ? "Selected" : null;
            }
        }
    }

    private void UpdateAccentPickerEnabledState()
    {
        bool manual = !ToggleSystemAccent.IsOn;
        AccentPickerCard.IsEnabled = manual;
        AccentPickerCard.Opacity = manual ? 1.0 : 0.5;
    }

    private void UpdateWidgetSubSettingsState()
    {
        bool enabled = ToggleShowWidget.IsOn;
        WidgetSubSettingsPanel.IsEnabled = enabled;
        WidgetSubSettingsPanel.Opacity = enabled ? 1.0 : 0.5;
    }

    private void UpdateHotkeyConflictBanner()
    {
        bool showConflict = ToggleRecallHotkey.IsOn && _hotkeyFailedProvider?.Invoke() == true;
        HotkeyConflictBanner.Visibility = showConflict ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHistoryCountLabel()
    {
        CardClearHistory.Description = _loc.Get("Setting_ClearHistory_Desc", HistoryService.Instance.Count);
    }

    private void SettingChanged_Event(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        UpdateAccentPickerEnabledState();
        UpdateWidgetSubSettingsState();
        SaveAllSettings();
        UpdateHotkeyConflictBanner();
    }

    private void DebouncedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        TxtOpacityVal.Text = $"{SliderOpacity.Value:F0}%";
        TxtDurationVal.Text = $"{SliderDuration.Value:F1}s";
        TxtHoverDurationVal.Text = $"{SliderHoverDuration.Value:F1}s";
        TxtWidgetOffsetVal.Text = $"{SliderWidgetOffset.Value:F0}px";

        _sliderDebounceTimer.Stop();
        _sliderDebounceTimer.Start();
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Background: SolidColorBrush scb })
        {
            string hex = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            SyncAccentControls(hex);
            ThemeService.Instance.UpdateAccentResource(scb.Color);
            SaveAllSettings();
        }
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || _isSyncingColor) return;

        byte r = (byte)Math.Round(SliderColorR.Value);
        byte g = (byte)Math.Round(SliderColorG.Value);
        byte b = (byte)Math.Round(SliderColorB.Value);
        var color = Color.FromRgb(r, g, b);
        string hex = $"#{r:X2}{g:X2}{b:X2}";

        _isSyncingColor = true;
        try
        {
            ValColorR.Text = r.ToString(CultureInfo.InvariantCulture);
            ValColorG.Text = g.ToString(CultureInfo.InvariantCulture);
            ValColorB.Text = b.ToString(CultureInfo.InvariantCulture);
            TextHexCode.Text = hex;
            CurrentAccentPreview.Background = ThemeService.CreateFrozenBrush(color);
            UpdateSelectedSwatchHighlight(hex);
        }
        finally
        {
            _isSyncingColor = false;
        }

        // Instant visual feedback + 150ms debounced persistence
        ThemeService.Instance.UpdateAccentResource(color);
        _sliderDebounceTimer.Stop();
        _sliderDebounceTimer.Start();
    }

    private void TextHexCode_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _isSyncingColor) return;

        string text = TextHexCode.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length == 7)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(text);
                _isSyncingColor = true;
                try
                {
                    SliderColorR.Value = c.R;
                    SliderColorG.Value = c.G;
                    SliderColorB.Value = c.B;
                    ValColorR.Text = c.R.ToString(CultureInfo.InvariantCulture);
                    ValColorG.Text = c.G.ToString(CultureInfo.InvariantCulture);
                    ValColorB.Text = c.B.ToString(CultureInfo.InvariantCulture);
                    CurrentAccentPreview.Background = ThemeService.CreateFrozenBrush(c);
                    UpdateSelectedSwatchHighlight(text.ToUpperInvariant());
                }
                finally
                {
                    _isSyncingColor = false;
                }

                ThemeService.Instance.UpdateAccentResource(c);
                _sliderDebounceTimer.Stop();
                _sliderDebounceTimer.Start();
            }
            catch
            {
            }
        }
    }

    private void SaveAllSettings()
    {
        var chosenLang = (AppLanguage)Math.Max(0, CmbLanguage.SelectedIndex);
        var chosenTheme = (AppThemeMode)Math.Max(0, CmbTheme.SelectedIndex);
        string hexColor = $"#{(byte)Math.Round(SliderColorR.Value):X2}{(byte)Math.Round(SliderColorG.Value):X2}{(byte)Math.Round(SliderColorB.Value):X2}";

        _isSaving = true;
        try
        {
            _settings.UpdateSettings(s =>
            {
                s.IsMonitoringEnabled = ToggleMonitoring.IsOn;
                s.LaunchOnStartup = ToggleStartup.IsOn;
                s.EnableRecallHotkey = ToggleRecallHotkey.IsOn;
                s.Language = chosenLang;

                s.Theme = chosenTheme;
                s.UseSystemAccentColor = ToggleSystemAccent.IsOn;
                s.AccentColor = hexColor;
                s.Placement = (FlyoutPlacement)Math.Max(0, CmbPlacement.SelectedIndex);
                s.OpacityPercent = Math.Round(SliderOpacity.Value);
                s.DisplayDurationSeconds = Math.Round(SliderDuration.Value, 1);
                s.HoverLeaveDurationSeconds = Math.Round(SliderHoverDuration.Value, 1);
                s.SuppressInFullscreen = ToggleSuppressFullscreen.IsOn;

                s.ShowTaskbarWidget = ToggleShowWidget.IsOn;
                s.WidgetPosition = (WidgetPositionMode)Math.Max(0, CmbWidgetPosition.SelectedIndex);
                s.WidgetAutoAlign = ToggleWidgetAutoAlign.IsOn;
                s.WidgetMonitor = (WidgetMonitorTarget)Math.Max(0, CmbWidgetMonitor.SelectedIndex);
                s.WidgetTextColor = (WidgetTextColorMode)Math.Max(0, CmbWidgetTextColor.SelectedIndex);
                s.WidgetOffsetX = Math.Round(SliderWidgetOffset.Value);

                s.DetectHexColor = ToggleDetectHex.IsOn;
                s.DetectUrl = ToggleDetectUrl.IsOn;
                s.DetectFileDrop = ToggleDetectFileDrop.IsOn;
                s.DetectImage = ToggleDetectImage.IsOn;
                s.DetectJson = ToggleDetectJson.IsOn;
                s.DetectEmail = ToggleDetectEmail.IsOn;
                s.DetectTimestamp = ToggleDetectTimestamp.IsOn;
                s.DetectIsoDateTime = ToggleDetectIsoDate.IsOn;
                s.DetectBase64 = ToggleDetectBase64.IsOn;
                s.DetectTable = ToggleDetectTable.IsOn;
                s.DetectCode = ToggleDetectCode.IsOn;
                s.DetectPlainText = ToggleDetectPlainText.IsOn;
                s.EnableCleanUrl = ToggleCleanUrl.IsOn;
                s.EnableCaseConverter = ToggleCaseConverter.IsOn;
                s.EnableJwtDetector = ToggleJwt.IsOn;
                s.EnableOcr = ToggleEnableOcr.IsOn;
                s.DetectFilePath = ToggleDetectFilePath.IsOn;
                s.DetectMath = ToggleDetectMath.IsOn;
                s.DetectUuid = ToggleDetectUuid.IsOn;

                s.IgnorePasswordManagers = ToggleIgnorePasswordManagers.IsOn;
                s.ExcludedApps = TxtExcludedApps.Text.Trim();
                s.PersistHistory = TogglePersistHistory.IsOn;
                s.AutomaticallyInstallUpdates = ToggleAutoUpdate.IsOn;
            });

            if (!TogglePersistHistory.IsOn)
            {
                HistoryService.Instance.DeletePersistedFile();
            }
            else
            {
                HistoryService.Instance.FlushPersistence();
            }

            if (_loc.CurrentLanguage != chosenLang)
            {
                _loc.CurrentLanguage = chosenLang;
            }
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        HistoryService.Instance.Clear(includePinned: true);
        HistoryService.Instance.DeletePersistedFile();
        UpdateHistoryCountLabel();
        CardClearHistory.Description = _loc.Get("Setting_ClearHistory_Done");
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdates) return;
        _isCheckingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        BtnInstallUpdateNow.Visibility = Visibility.Collapsed;
        ShowUpdateStatus("\uE895", _loc.Get("Update_Checking"), "TextSecondaryBrush");

        try
        {
            _pendingUpdate = await UpdateService.Instance.CheckForUpdateAsync(CancellationToken.None);
            if (_pendingUpdate != null)
            {
                ShowUpdateStatus("\uE896", _loc.Get("Update_Available", _pendingUpdate.Version), "FluentAccentBrush");
                BtnInstallUpdateNow.Visibility = Visibility.Visible;
            }
            else
            {
                ShowUpdateStatus("\uE73E", _loc.Get("Update_UpToDate"), "StatusSuccessBrush");
            }
        }
        catch
        {
            ShowUpdateStatus("\uE7BA", _loc.Get("Update_Failed"), "StatusErrorBrush");
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void BtnInstallUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        BtnInstallUpdateNow.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;

        var progress = new Progress<double>(ratio =>
        {
            int pct = (int)Math.Round(ratio * 100);
            string msg = pct >= 100 ? _loc.Get("Update_Verifying") : _loc.Get("Update_Downloading", pct);
            ShowUpdateStatus("\uE896", msg, "FluentAccentBrush");
        });

        ShowUpdateStatus("\uE896", _loc.Get("Update_Downloading", 0), "FluentAccentBrush");

        try
        {
            await UpdateService.Instance.DownloadAndStartInstallerAsync(_pendingUpdate, CancellationToken.None, progress);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ShowUpdateStatus("\uE7BA", $"{_loc.Get("Update_Failed")} ({ex.Message})", "StatusErrorBrush");
            BtnInstallUpdateNow.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowUpdateStatus(string glyph, string message, string brushKey)
    {
        UpdateStatusGlyph.Text = glyph;
        UpdateStatusGlyph.Foreground = TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        UpdateStatusText.Text = message;
        UpdateStatusBanner.Visibility = Visibility.Visible;
    }

    private void GithubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/featlis/ClipFlyout",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = FluentDialog.ShowConfirm(
            this,
            _loc.Get("About_Reset"),
            _loc.Get("About_Reset_Confirm"),
            _loc.Get("Dialog_Confirm"),
            _loc.Get("Dialog_Cancel"),
            "\uE777");

        if (confirmed)
        {
            var defaults = new AppSettings { IsFirstRun = false };
            _isSaving = true;
            try
            {
                _settings.SaveSettings(defaults);
            }
            finally
            {
                _isSaving = false;
            }
            HistoryService.Instance.DeletePersistedFile();
            _loc.CurrentLanguage = defaults.Language;
            LoadValuesFromSettings();
            ApplyLocalization();
            UpdateHotkeyConflictBanner();
        }
    }

    private sealed record SnippetSettingsRow(
        SnippetItem Item,
        string Title,
        string Preview,
        string EditLabel,
        string CopyLabel,
        string DeleteLabel);

    private void UpdateHotkeyLabels()
    {
        var cfg = _settings.Current;
        string display = AppSettings.FormatHotkeyDisplay(cfg.RecallHotkeyModifiers, cfg.RecallHotkeyKey);
        CardRecallHotkey.Header = _loc.Get("Setting_EnableRecallHotkey", display);
        HotkeyConflictText.Text = _loc.Get("Setting_HotkeyConflict", display);
        BtnRecordHotkey.Content = _isRecordingHotkey ? _loc.Get("Hotkey_Record_Recording") : display;
    }

    private void BtnRecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = !_isRecordingHotkey;
        UpdateHotkeyLabels();
    }

    private void BtnResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        _isSaving = true;
        try
        {
            _settings.UpdateSettings(s =>
            {
                s.RecallHotkeyModifiers = Win32.MOD_ALT | Win32.MOD_SHIFT;
                s.RecallHotkeyKey = (uint)'C';
            });
        }
        finally
        {
            _isSaving = false;
        }
        UpdateHotkeyLabels();
        UpdateHotkeyConflictBanner();
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecordingHotkey) return;

        e.Handled = true;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

        if (actualKey == Key.Escape)
        {
            _isRecordingHotkey = false;
            UpdateHotkeyLabels();
            return;
        }

        if (actualKey is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.DeadCharProcessed or Key.ImeProcessed or Key.None)
        {
            return;
        }

        uint mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= Win32.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= Win32.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= Win32.MOD_SHIFT;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0 ||
            Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            mods |= Win32.MOD_WIN;
        }

        int vk = KeyInterop.VirtualKeyFromKey(actualKey);
        if (mods == 0 || vk <= 0 || vk > 0xFE) return;

        _isRecordingHotkey = false;
        _isSaving = true;
        try
        {
            _settings.UpdateSettings(s =>
            {
                s.RecallHotkeyModifiers = mods;
                s.RecallHotkeyKey = (uint)vk;
            });
        }
        finally
        {
            _isSaving = false;
        }

        UpdateHotkeyLabels();
        UpdateHotkeyConflictBanner();
    }

    private void ExcludedApps_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _sliderDebounceTimer.Stop();
        _sliderDebounceTimer.Start();
    }

    private void RefreshSnippetsList()
    {
        if (SnippetsSettingsList == null) return;
        var items = SnippetService.Instance.GetItems();
        LblSavedSnippetsHeader.Text = _loc.Get("Snippets_List_Header", items.Count);
        TxtNoSnippetsSettings.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        string editLbl = _loc.Get("Snippets_Edit");
        string copyLbl = _loc.Get("Snippets_Copy");
        string delLbl = _loc.Get("Snippets_Delete");

        SnippetsSettingsList.ItemsSource = items.Select(s => new SnippetSettingsRow(
            s,
            string.IsNullOrWhiteSpace(s.Title) ? SnippetService.BuildDefaultTitle(s.Content) : s.Title,
            HistoryService.NormalizeSingleLine(s.Content),
            editLbl,
            copyLbl,
            delLbl
        )).ToList();
    }

    private void TxtSnippetField_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSnippetTitlePlaceholder.Visibility = string.IsNullOrEmpty(TxtSnippetTitle.Text) ? Visibility.Visible : Visibility.Collapsed;
        TxtSnippetContentPlaceholder.Visibility = string.IsNullOrEmpty(TxtSnippetContent.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnSaveSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtSnippetContent.Text)) return;

        SnippetService.Instance.AddOrUpdate(TxtSnippetTitle.Text, TxtSnippetContent.Text, _editingSnippetId);
        ResetSnippetEditor();
        RefreshSnippetsList();
    }

    private void BtnCancelSnippetEdit_Click(object sender, RoutedEventArgs e)
    {
        ResetSnippetEditor();
    }

    private void ResetSnippetEditor()
    {
        _editingSnippetId = null;
        TxtSnippetTitle.Text = string.Empty;
        TxtSnippetContent.Text = string.Empty;
        BtnSaveSnippet.Content = _loc.Get("Snippets_Save");
        BtnCancelSnippetEdit.Visibility = Visibility.Collapsed;
    }

    private void SnippetRowEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnippetItem item })
        {
            _editingSnippetId = item.Id;
            TxtSnippetTitle.Text = item.Title;
            TxtSnippetContent.Text = item.Content;
            BtnSaveSnippet.Content = _loc.Get("Snippets_Update");
            BtnCancelSnippetEdit.Visibility = Visibility.Visible;
            TxtSnippetContent.Focus();
        }
    }

    private void SnippetRowCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnippetItem item })
        {
            try
            {
                Clipboard.SetDataObject(item.Content, true);
            }
            catch
            {
            }
        }
    }

    private void SnippetRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnippetItem item })
        {
            if (_editingSnippetId == item.Id) ResetSnippetEditor();
            SnippetService.Instance.Delete(item.Id);
            RefreshSnippetsList();
        }
    }
}
