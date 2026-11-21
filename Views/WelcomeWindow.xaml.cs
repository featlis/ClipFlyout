using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public partial class WelcomeWindow : Window
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private bool _isInitializing = true;
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly Action _themeChangedHandler;

    public event Action? CustomizeRequested;

    public WelcomeWindow()
    {
        InitializeComponent();

        try
        {
            var iconSrc = AppIconHelper.GetAppIconBitmapSource(128);
            Icon = iconSrc;
            WelcomeAppIcon.Source = iconSrc;
        }
        catch { }

        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(ApplyBackdrop);
        };

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => ThemeService.Instance.ThemeChanged -= _themeChangedHandler;
        ThemeService.Instance.ThemeChanged += _themeChangedHandler;

        ApplyLocalization();
        LoadDefaults();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32.EnableMica(_hwnd, ThemeService.Instance.IsDarkTheme);
        }
    }

    private void LoadDefaults()
    {
        _isInitializing = true;
        try
        {
            var cfg = _settings.Current;
            ToggleStartup.IsOn = cfg.LaunchOnStartup || SettingsService.IsStartupConfigured();
            CmbLanguage.SelectedIndex = (int)cfg.Language;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void ApplyLocalization()
    {
        bool prev = _isInitializing;
        _isInitializing = true;
        try
        {
            Title = _loc.Get("Welcome_Title");
            WelcomeTitle.Text = _loc.Get("Welcome_Title");
            WelcomeSubtitle.Text = _loc.Get("Welcome_Subtitle");

            CardFeat1.Header = _loc.Get("Welcome_Feat1_Title");
            CardFeat1.Description = _loc.Get("Welcome_Feat1_Desc");
            CardFeat2.Header = _loc.Get("Welcome_Feat2_Title");
            CardFeat2.Description = _loc.Get("Welcome_Feat2_Desc");
            CardFeat3.Header = _loc.Get("Welcome_Feat3_Title");
            CardFeat3.Description = _loc.Get("Welcome_Feat3_Desc");

            CardOptLanguage.Header = _loc.Get("Welcome_Language");
            CardOptLanguage.Description = _loc.Get("Welcome_Language_Desc");
            CardOptStartup.Header = _loc.Get("Setting_Startup");
            CardOptStartup.Description = _loc.Get("Setting_Startup_Desc");

            BtnCustomize.Content = _loc.Get("Welcome_Customize");
            BtnUseDefaults.Content = _loc.Get("Welcome_UseDefaults");

            int selected = Math.Max(0, (int)_settings.Current.Language);
            CmbLanguage.Items.Clear();
            CmbLanguage.Items.Add(_loc.Get("Lang_Auto"));
            CmbLanguage.Items.Add(_loc.Get("Lang_Ja"));
            CmbLanguage.Items.Add(_loc.Get("Lang_En"));
            CmbLanguage.SelectedIndex = selected;
        }
        finally
        {
            _isInitializing = prev;
        }
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbLanguage.SelectedIndex < 0) return;

        var lang = (AppLanguage)CmbLanguage.SelectedIndex;
        _settings.UpdateSettings(s => s.Language = lang);
        _loc.CurrentLanguage = lang;
        ApplyLocalization();
    }

    private void SaveWelcomeChoices()
    {
        var lang = (AppLanguage)Math.Max(0, CmbLanguage.SelectedIndex);
        _settings.UpdateSettings(s =>
        {
            s.IsFirstRun = false;
            s.LaunchOnStartup = ToggleStartup.IsOn;
            s.Language = lang;
        });
    }

    private void BtnUseDefaults_Click(object sender, RoutedEventArgs e)
    {
        SaveWelcomeChoices();
        Close();
    }

    private void BtnCustomize_Click(object sender, RoutedEventArgs e)
    {
        SaveWelcomeChoices();
        CustomizeRequested?.Invoke();
        Close();
    }
}
