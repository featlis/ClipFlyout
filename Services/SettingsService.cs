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

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;
    private readonly bool _syncStartupRegistry;
    private readonly object _lock = new();
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
        AppSettings result;
        bool loadedFromDisk = false;
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    result = loaded.Normalize();
                    loadedFromDisk = true;
                }
                else
                {
                    result = new AppSettings().Normalize();
                }
            }
            else
            {
                result = new AppSettings().Normalize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            result = new AppSettings().Normalize();
        }

        // Only inherit OS startup state on brand-new installs where no settings file exists yet.
        // Never overwrite an explicit user preference saved on disk.
        if (!loadedFromDisk && !result.LaunchOnStartup && _syncStartupRegistry && IsStartupConfigured())
        {
            result.LaunchOnStartup = true;
        }

        return result;
    }

    public static bool IsStartupConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            if (key?.GetValue("ClipFlyout") != null) return true;

            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!string.IsNullOrEmpty(startupDir))
            {
                if (File.Exists(Path.Combine(startupDir, "ClipFlyout.lnk"))) return true;
                if (File.Exists(Path.Combine(startupDir, "ClipFlyout.exe.lnk"))) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    public void SaveSettings(AppSettings settings)
    {
        AppSettings? snapshotToNotify = null;
        bool syncStartup = false;
        bool targetStartupState = false;

        lock (_lock)
        {
            try
            {
                var normalized = settings.Normalize();
                bool previousStartup = _currentSettings.LaunchOnStartup;

                string json = JsonSerializer.Serialize(normalized, SerializerOptions);
                string temporaryFilePath = _settingsFilePath + $".{Guid.NewGuid():N}.tmp";
                File.WriteAllText(temporaryFilePath, json);
                File.Move(temporaryFilePath, _settingsFilePath, true);

                _currentSettings = normalized;
                snapshotToNotify = normalized.Clone();

                if (_syncStartupRegistry && previousStartup != normalized.LaunchOnStartup)
                {
                    syncStartup = true;
                    targetStartupState = normalized.LaunchOnStartup;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        // Perform registry sync & event dispatch OUTSIDE lock to prevent UI/background deadlocks
        if (syncStartup)
        {
            SyncStartupRegistry(targetStartupState);
        }

        if (snapshotToNotify != null)
        {
            SettingsChanged?.Invoke(snapshotToNotify);
        }
    }

    public void UpdateSettings(Action<AppSettings> updateAction)
    {
        AppSettings updated;
        lock (_lock)
        {
            updated = _currentSettings.Clone();
            updateAction(updated);
        }
        SaveSettings(updated);
    }

    private void SyncStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            string? exePath = Environment.ProcessPath;

            if (enable)
            {
                if (key != null && !string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("ClipFlyout", $"\"{exePath}\"");
                }
            }
            else
            {
                if (key?.GetValue("ClipFlyout") != null)
                {
                    key.DeleteValue("ClipFlyout", false);
                }

                string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrEmpty(startupDir))
                {
                    string linkPath1 = Path.Combine(startupDir, "ClipFlyout.lnk");
                    if (File.Exists(linkPath1)) { try { File.Delete(linkPath1); } catch { } }
                    string linkPath2 = Path.Combine(startupDir, "ClipFlyout.exe.lnk");
                    if (File.Exists(linkPath2)) { try { File.Delete(linkPath2); } catch { } }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to sync startup registry: {ex.Message}");
        }
    }
}
