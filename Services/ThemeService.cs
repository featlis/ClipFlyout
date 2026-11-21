using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClipFlyout.Models;
using Microsoft.Win32;

namespace ClipFlyout.Services;

public class ThemeService : IDisposable
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    private AppThemeMode _mode = AppThemeMode.System;
    private bool _isDarkTheme;
    private bool _isTransparencyEnabled = true;

    public event Action? ThemeChanged;

    public AppThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                UpdateThemeResolution();
            }
        }
    }

    public bool IsDarkTheme => _isDarkTheme;
    public bool IsTransparencyEnabled => _isTransparencyEnabled;

    public Color AccentColor
    {
        get
        {
            var current = SettingsService.Instance.Current;
            if (current.UseSystemAccentColor && TryGetWindowsAccentColor(out var sysColor))
            {
                return sysColor;
            }

            try
            {
                return (Color)ColorConverter.ConvertFromString(current.AccentColor);
            }
            catch
            {
                return Color.FromRgb(0, 120, 212);
            }
        }
    }

    public Color TextOnAccentColor => GetContrastingForeground(AccentColor);

    private readonly Action<AppSettings> _settingsChangedHandler;

    public ThemeService()
    {
        _mode = SettingsService.Instance.Current.Theme;
        _settingsChangedHandler = cfg =>
        {
            if (_mode != cfg.Theme)
            {
                _mode = cfg.Theme;
                UpdateThemeResolution();
            }
            else
            {
                UpdateThemeResources(_isDarkTheme, AccentColor);
                ThemeChanged?.Invoke();
            }
        };
        SettingsService.Instance.SettingsChanged += _settingsChangedHandler;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateThemeResolution();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.BeginInvoke(UpdateThemeResolution);
            }
        }
    }

    public void UpdateThemeResolution()
    {
        if (_mode == AppThemeMode.Dark)
        {
            _isDarkTheme = true;
        }
        else if (_mode == AppThemeMode.Light)
        {
            _isDarkTheme = false;
        }
        else
        {
            _isDarkTheme = GetWindowsIsDarkTheme();
        }

        _isTransparencyEnabled = GetWindowsIsTransparencyEnabled();
        UpdateThemeResources(_isDarkTheme, AccentColor);

        ThemeChanged?.Invoke();
    }

    public void UpdateAccentResource(Color color)
    {
        UpdateThemeResources(_isDarkTheme, color);
    }

    private void UpdateThemeResources(bool isDark, Color accent)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted) return;

        void Apply()
        {
            try
            {
                ApplyTokenDictionary(app, isDark);
                ApplyAccentBrushes(app, accent, isDark);
            }
            catch
            {
            }
        }

        if (app.Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            app.Dispatcher.BeginInvoke(Apply);
        }
    }

    private static void ApplyTokenDictionary(Application app, bool isDark)
    {
        string targetSuffix = isDark ? "Themes/Tokens.Dark.xaml" : "Themes/Tokens.Light.xaml";
        var targetUri = new Uri($"pack://application:,,,/ClipFlyout;component/{targetSuffix}", UriKind.Absolute);

        // Load token values from XAML and assign directly onto Application.Resources.
        // Never replace/remove MergedDictionaries entries at runtime, which can trigger
        // DeferredAppResourceReference NREs when combined with .NET 10 WPF ThemeMode.
        var tokenDict = new ResourceDictionary { Source = targetUri };
        foreach (var key in tokenDict.Keys)
        {
            object? val = tokenDict[key];
            if (val is Freezable freezable && freezable.CanFreeze && !freezable.IsFrozen)
            {
                freezable.Freeze();
            }
            app.Resources[key] = val;
        }
    }

    private static void ApplyAccentBrushes(Application app, Color accent, bool isDark)
    {
        Color hover = isDark ? AdjustBrightness(accent, 1.14) : AdjustBrightness(accent, 0.90);
        Color pressed = isDark ? AdjustBrightness(accent, 0.84) : AdjustBrightness(accent, 0.78);
        Color onAccent = GetContrastingForeground(accent);

        app.Resources["FluentAccentBrush"] = CreateFrozenBrush(accent);
        app.Resources["FluentAccentHoverBrush"] = CreateFrozenBrush(hover);
        app.Resources["FluentAccentPressedBrush"] = CreateFrozenBrush(pressed);
        app.Resources["FluentAccentSubtleBrush"] = CreateFrozenBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
        app.Resources["FluentAccentLightBrush"] = CreateFrozenBrush(Color.FromArgb(44, accent.R, accent.G, accent.B));
        app.Resources["FluentTextOnAccentBrush"] = CreateFrozenBrush(onAccent);
    }

    public static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Computes WCAG 2.1 relative luminance to pick crisp black or white foreground text on any accent color.
    /// </summary>
    public static Color GetContrastingForeground(Color background)
    {
        static double Linearize(byte c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        double luminance = 0.2126 * Linearize(background.R) +
                           0.7152 * Linearize(background.G) +
                           0.0722 * Linearize(background.B);

        // Contrast ratio threshold favoring crisp legibility on bright accents (yellow, cyan, mint)
        return luminance > 0.36 ? Color.FromRgb(16, 16, 16) : Color.FromRgb(255, 255, 255);
    }

    public static Color AdjustBrightness(Color color, double factor)
    {
        byte r = (byte)Math.Clamp((int)Math.Round(color.R * factor), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(color.G * factor), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(color.B * factor), 0, 255);
        return Color.FromArgb(color.A, r, g, b);
    }

    public static bool TryGetWindowsAccentColor(out Color color)
    {
        color = Color.FromRgb(0, 120, 212);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            var val = key?.GetValue("AccentColor") ?? key?.GetValue("ColorizationColor");
            if (val is int abgr)
            {
                // AccentColor is stored as ABGR
                byte r = (byte)(abgr & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                if (key?.GetValue("AccentColor") == null)
                {
                    // ColorizationColor is stored as ARGB
                    r = (byte)((abgr >> 16) & 0xFF);
                    g = (byte)((abgr >> 8) & 0xFF);
                    b = (byte)(abgr & 0xFF);
                }
                color = Color.FromRgb(r, g, b);
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool GetWindowsIsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsValue = key?.GetValue("AppsUseLightTheme");
            if (appsValue is int appsInt)
            {
                return appsInt == 0;
            }

            var sysValue = key?.GetValue("SystemUsesLightTheme");
            if (sysValue is int sysInt)
            {
                return sysInt == 0;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool GetWindowsIsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("EnableTransparency");
            if (value is int intVal)
            {
                return intVal != 0;
            }
        }
        catch
        {
        }
        return true;
    }

    public void Dispose()
    {
        SettingsService.Instance.SettingsChanged -= _settingsChangedHandler;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
