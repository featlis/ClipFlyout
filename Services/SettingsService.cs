using System;
using System.IO;
using System.Text.Json;
using ClipFlyout.Models;
using Microsoft.Win32;

namespace ClipFlyout.Services;

public class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private readonly string _settingsFilePath;
    private readonly bool _syncStartupRegistry;
    private volatile AppSettings _currentSettings;

    public event Action<AppSettings>? SettingsChanged;

    public AppSettings Current => _currentSettings.Clone();

    public SettingsService(string? settingsFilePath = null, bool syncStartupRegistry = true)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(localAppData, "ClipFlyout");
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }
        else
        {
            _settingsFilePath = Path.GetFullPath(settingsFilePath);
        }

        string? settingsDirectory = Path.GetDirectoryName(_settingsFilePath);
        if (string.IsNullOrEmpty(settingsDirectory))
        {
            throw new ArgumentException("The settings file must have a directory.", nameof(settingsFilePath));
        }

        Directory.CreateDirectory(settingsDirectory);
        _syncStartupRegistry = syncStartupRegistry;

        _currentSettings = LoadSettings();
        if (_syncStartupRegistry)
        {
            SyncStartupRegistry(_currentSettings.LaunchOnStartup);
        }
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded.Normalize();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings().Normalize();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            _currentSettings = settings.Normalize();
            string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
            string temporaryFilePath = _settingsFilePath + ".tmp";
            File.WriteAllText(temporaryFilePath, json);
            File.Move(temporaryFilePath, _settingsFilePath, true);

            if (_syncStartupRegistry)
            {
                SyncStartupRegistry(_currentSettings.LaunchOnStartup);
            }

            SettingsChanged?.Invoke(_currentSettings.Clone());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public void UpdateSettings(Action<AppSettings> updateAction)
    {
        var copy = _currentSettings.Clone();
        updateAction(copy);
        SaveSettings(copy);
    }

    private void SyncStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            if (enable)
            {
                key.SetValue("ClipFlyout", $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue("ClipFlyout") != null)
                {
                    key.DeleteValue("ClipFlyout", false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to sync startup registry: {ex.Message}");
        }
    }
}
