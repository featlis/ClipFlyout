using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Views;

public partial class WelcomeWindow : Window
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public event Action? CustomizeRequested;

    private System.Drawing.Icon? _iconBig;
    private System.Drawing.Icon? _iconSmall;
    private bool _isInitializingLanguage;
    private readonly Action _themeChangedHandler;
    private readonly Action _languageChangedHandler;

    public WelcomeWindow()
    {
        InitializeComponent();
        try { Icon = AppIconHelper.CreateAppBitmapSource(64); } catch { }

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            WelcomeAppIcon.Source = AppIconHelper.CreateAppBitmapSource(64);
            ToggleStartup.IsOn = _settings.Current.LaunchOnStartup;

            _isInitializingLanguage = true;
            var currentLang = _settings.Current.Language;
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag is string tag && Enum.TryParse<AppLanguage>(tag, out var lang) && lang == currentLang)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }
            if (CmbLanguage.SelectedIndex < 0) CmbLanguage.SelectedIndex = 0;
            _isInitializingLanguage = false;

            Activate();
            Focus();
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                Win32.SetForegroundWindow(helper.Handle);
            }
        };

        _themeChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ApplyTheme);
            }
        };
        _languageChangedHandler = () =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(ApplyLocalization);
            }
        };

        _theme.ThemeChanged += _themeChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        Closed += (_, _) =>
        {
            _theme.ThemeChanged -= _themeChangedHandler;
            _loc.LanguageChanged -= _languageChangedHandler;
            _settings.UpdateSettings(s => s.IsFirstRun = false);
            _iconBig?.Dispose();
            _iconSmall?.Dispose();
        };

        ApplyTheme();
        ApplyLocalization();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        Win32.EnableMica(helper.Handle, isDark: false);

        try
        {
            _iconBig = AppIconHelper.CreateAppIcon(64);
            _iconSmall = AppIconHelper.CreateAppIcon(16);

            Win32.SendMessage(helper.Handle, Win32.WM_SETICON, (IntPtr)Win32.ICON_BIG, _iconBig.Handle);
            Win32.SendMessage(helper.Handle, Win32.WM_SETICON, (IntPtr)Win32.ICON_SMALL, _iconSmall.Handle);
            Win32.SetClassLongPtr(helper.Handle, Win32.GCLP_HICON, _iconBig.Handle);
            Win32.SetClassLongPtr(helper.Handle, Win32.GCLP_HICONSM, _iconSmall.Handle);
        }
        catch { }
    }

    public void ApplyTheme()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            Win32.EnableMica(helper.Handle, isDark: false);
        }

        var accent = _theme.AccentColor;

        // Welcome / initial setup wizard always uses Light Mode palette
        RootGrid.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        WelcomeTitle.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        WelcomeSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        var cardBg = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        var cardBorder = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        var titleFg = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var descFg = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        CardFeature1.Background = cardBg; CardFeature1.BorderBrush = cardBorder;
        Feat1Title.Foreground = titleFg; Feat1Desc.Foreground = descFg;

        CardFeature2.Background = cardBg; CardFeature2.BorderBrush = cardBorder;
        Feat2Title.Foreground = titleFg; Feat2Desc.Foreground = descFg;

        CardFeature3.Background = cardBg; CardFeature3.BorderBrush = cardBorder;
        Feat3Title.Foreground = titleFg; Feat3Desc.Foreground = descFg;

        CardOptionLanguage.Background = cardBg; CardOptionLanguage.BorderBrush = cardBorder;
        OptLanguageTitle.Foreground = titleFg; OptLanguageDesc.Foreground = descFg;

        CardOptionStartup.Background = cardBg; CardOptionStartup.BorderBrush = cardBorder;
        OptStartupTitle.Foreground = titleFg; OptStartupDesc.Foreground = descFg;

        FooterBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        FooterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));

        BtnCustomize.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        BtnCustomize.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        BtnCustomize.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));

        // Accent Primary Button
        BtnUseDefaults.Background = new SolidColorBrush(accent);
        BtnUseDefaults.BorderBrush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Min(255, accent.R + 25),
            (byte)Math.Min(255, accent.G + 25),
            (byte)Math.Min(255, accent.B + 25)));
        BtnUseDefaults.Foreground = Brushes.White;
    }

    public void ApplyLocalization()
    {
        Title = _loc.Get("Welcome_Title");
        WelcomeTitle.Text = _loc.Get("Welcome_Title");
        WelcomeSubtitle.Text = _loc.Get("Welcome_Subtitle");

        Feat1Title.Text = _loc.Get("Welcome_Feat1_Title");
        Feat1Desc.Text = _loc.Get("Welcome_Feat1_Desc");

        Feat2Title.Text = _loc.Get("Welcome_Feat2_Title");
        Feat2Desc.Text = _loc.Get("Welcome_Feat2_Desc");

        Feat3Title.Text = _loc.Get("Welcome_Feat3_Title");
        Feat3Desc.Text = _loc.Get("Welcome_Feat3_Desc");

        OptLanguageTitle.Text = _loc.Get("Welcome_Language");
        OptLanguageDesc.Text = _loc.Get("Welcome_Language_Desc");

        OptStartupTitle.Text = _loc.Get("Setting_Startup");
        OptStartupDesc.Text = _loc.Get("Setting_Startup_Desc");

        BtnCustomize.Content = _loc.Get("Welcome_Customize");
        BtnUseDefaults.Content = _loc.Get("Welcome_UseDefaults");
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingLanguage) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse<AppLanguage>(tag, out var lang))
        {
            _settings.UpdateSettings(s => s.Language = lang);
            _loc.CurrentLanguage = lang;
            ApplyLocalization();
        }
    }

    private void BtnUseDefaults_Click(object sender, RoutedEventArgs e)
    {
        bool startup = ToggleStartup.IsOn;
        var lang = _loc.CurrentLanguage;
        _settings.UpdateSettings(s =>
        {
            s.IsFirstRun = false;
            s.Language = lang;
            s.LaunchOnStartup = startup;
            s.Theme = AppThemeMode.System;
            s.ShowTaskbarWidget = true;
            s.IsMonitoringEnabled = true;
        });
        Close();
    }

    private void BtnCustomize_Click(object sender, RoutedEventArgs e)
    {
        bool startup = ToggleStartup.IsOn;
        var lang = _loc.CurrentLanguage;
        _settings.UpdateSettings(s =>
        {
            s.IsFirstRun = false;
            s.Language = lang;
            s.LaunchOnStartup = startup;
        });
        Close();
        CustomizeRequested?.Invoke();
    }
}
