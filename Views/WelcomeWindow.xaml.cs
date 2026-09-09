using System;
using System.Windows;
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

    public WelcomeWindow()
    {
        InitializeComponent();
        try { Icon = AppIconHelper.CreateAppBitmapSource(32); } catch { }

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            WelcomeAppIcon.Source = AppIconHelper.CreateAppBitmapSource(64);
            ToggleStartup.IsOn = _settings.Current.LaunchOnStartup;
            Activate();
            Focus();
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                Win32.SetForegroundWindow(helper.Handle);
            }
        };

        _theme.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        _loc.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);

        ApplyTheme();
        ApplyLocalization();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        Win32.EnableMica(helper.Handle, _theme.IsDarkTheme);
    }

    public void ApplyTheme()
    {
        bool isDark = _theme.IsDarkTheme;
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            Win32.EnableMica(helper.Handle, isDark);
        }

        var accent = _theme.AccentColor;

        if (isDark)
        {
            RootGrid.Background = new SolidColorBrush(Color.FromRgb(26, 28, 35));
            WelcomeTitle.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            WelcomeSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

            var cardBg = new SolidColorBrush(Color.FromArgb(120, 36, 40, 52));
            var cardBorder = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            var titleFg = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            var descFg = new SolidColorBrush(Color.FromRgb(148, 163, 184));

            CardFeature1.Background = cardBg; CardFeature1.BorderBrush = cardBorder;
            Feat1Title.Foreground = titleFg; Feat1Desc.Foreground = descFg;

            CardFeature2.Background = cardBg; CardFeature2.BorderBrush = cardBorder;
            Feat2Title.Foreground = titleFg; Feat2Desc.Foreground = descFg;

            CardFeature3.Background = cardBg; CardFeature3.BorderBrush = cardBorder;
            Feat3Title.Foreground = titleFg; Feat3Desc.Foreground = descFg;

            CardOptionStartup.Background = cardBg; CardOptionStartup.BorderBrush = cardBorder;
            OptStartupTitle.Foreground = titleFg; OptStartupDesc.Foreground = descFg;

            FooterBorder.Background = new SolidColorBrush(Color.FromRgb(20, 22, 28));
            FooterBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

            BtnCustomize.Background = new SolidColorBrush(Color.FromRgb(38, 42, 53));
            BtnCustomize.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 88));
            BtnCustomize.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246));
        }
        else
        {
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

            CardOptionStartup.Background = cardBg; CardOptionStartup.BorderBrush = cardBorder;
            OptStartupTitle.Foreground = titleFg; OptStartupDesc.Foreground = descFg;

            FooterBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            FooterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            BtnCustomize.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            BtnCustomize.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            BtnCustomize.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        }

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

        OptStartupTitle.Text = _loc.Get("Setting_Startup");
        OptStartupDesc.Text = _loc.Get("Setting_Startup_Desc");

        BtnCustomize.Content = _loc.Get("Welcome_Customize");
        BtnUseDefaults.Content = _loc.Get("Welcome_UseDefaults");
    }

    private void BtnUseDefaults_Click(object sender, RoutedEventArgs e)
    {
        bool startup = ToggleStartup.IsOn;
        _settings.UpdateSettings(s =>
        {
            s.IsFirstRun = false;
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
        _settings.UpdateSettings(s =>
        {
            s.IsFirstRun = false;
            s.LaunchOnStartup = startup;
        });
        Close();
        CustomizeRequested?.Invoke();
    }
}
