using System;
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
            try
            {
                return (Color)ColorConverter.ConvertFromString(SettingsService.Instance.Current.AccentColor);
            }
            catch
            {
                return Color.FromRgb(0, 120, 212);
            }
        }
    }

    private readonly Action<AppSettings> _settingsChangedHandler;

    public ThemeService()
    {
        _mode = SettingsService.Instance.Current.Theme;
        _settingsChangedHandler = cfg =>
        {
            UpdateAccentResource(AccentColor);
            if (_mode != cfg.Theme)
            {
                _mode = cfg.Theme;
                UpdateThemeResolution();
            }
            else
            {
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
            Application.Current?.Dispatcher.Invoke(UpdateThemeResolution);
        }
    }

    public void UpdateThemeResolution()
    {
        bool wasDark = _isDarkTheme;
        bool wasTransparency = _isTransparencyEnabled;

        if (_mode == AppThemeMode.Dark)
        {
            _isDarkTheme = true;
        }
        else if (_mode == AppThemeMode.Light)
        {
            _isDarkTheme = false;
        }
        else // System
        {
            _isDarkTheme = GetWindowsIsDarkTheme();
        }

        _isTransparencyEnabled = GetWindowsIsTransparencyEnabled();
        UpdateAccentResource(AccentColor);

        ThemeChanged?.Invoke();
    }

    public void UpdateAccentResource(Color color)
    {
        if (Application.Current != null)
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    ApplyBrushes(color);
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(() => ApplyBrushes(color));
                }
            }
            catch { }
        }
    }

    private static void ApplyBrushes(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources["FluentAccentBrush"] = brush;
        var lightBrush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
        lightBrush.Freeze();
        Application.Current.Resources["FluentAccentLightBrush"] = lightBrush;
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
            // Default to light (standard Windows clean default) if cannot read
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
            // Default to true if cannot read
        }
        return true;
    }

    public void Dispose()
    {
        SettingsService.Instance.SettingsChanged -= _settingsChangedHandler;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
