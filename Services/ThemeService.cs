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

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateThemeResolution();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
        {
            if (_mode == AppThemeMode.System)
            {
                Application.Current?.Dispatcher.Invoke(UpdateThemeResolution);
            }
        }
    }

    public void UpdateThemeResolution()
    {
        bool wasDark = _isDarkTheme;

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

        ThemeChanged?.Invoke();
    }

    private static bool GetWindowsIsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intVal)
            {
                return intVal == 0;
            }
        }
        catch
        {
            // Default to dark if cannot read
        }
        return true;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
