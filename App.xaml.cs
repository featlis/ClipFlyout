using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipFlyout.Services;
using ClipFlyout.Views;
using WpfApplication = System.Windows.Application;

namespace ClipFlyout;

public partial class App : WpfApplication
{
    private ClipboardMonitor? _clipboardMonitor;
    private ActionExecutor? _actionExecutor;
    private DataTypeDetector? _detector;
    private FlyoutWindowManager? _windowManager;
    private TrayIconService? _trayIconService;
    private SettingsService? _settingsService;
    private ThemeService? _themeService;
    private TaskbarWidgetWindow? _taskbarWidget;
    private HotkeyService? _hotkeyService;
    private long _detectionGeneration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _settingsService = SettingsService.Instance;
            _themeService = ThemeService.Instance;

            // Apply saved user settings for theme and language
            _themeService.Mode = _settingsService.Current.Theme;
            LocalizationService.Instance.CurrentLanguage = _settingsService.Current.Language;

            _clipboardMonitor = new ClipboardMonitor
            {
                IsEnabled = _settingsService.Current.IsMonitoringEnabled
            };

            _actionExecutor = new ActionExecutor(_clipboardMonitor);
            _detector = new DataTypeDetector(_actionExecutor);
            _windowManager = new FlyoutWindowManager(_actionExecutor);
            _trayIconService = new TrayIconService(_clipboardMonitor);

            _taskbarWidget = new TaskbarWidgetWindow();
            _taskbarWidget.FlyoutRequested += result => _windowManager?.ShowFlyout(result);
            _taskbarWidget.SettingsRequested += () => _trayIconService?.OpenSettings();
            if (_settingsService.Current.ShowTaskbarWidget)
            {
                _taskbarWidget.Show();
            }

            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyPressed += () => _windowManager?.RecallLastFlyout();
            if (_settingsService.Current.EnableRecallHotkey)
            {
                _hotkeyService.Start();
            }

            _trayIconService.HistoryItemSelected += result => _windowManager?.ShowFlyout(result);

            if (ShouldCheckForUpdates(_settingsService.Current))
            {
                _ = CheckForAutomaticUpdateAsync();
            }

            _settingsService.SettingsChanged += cfg =>
            {
                if (_clipboardMonitor != null)
                {
                    _clipboardMonitor.IsEnabled = cfg.IsMonitoringEnabled;
                }
                if (_hotkeyService != null)
                {
                    if (cfg.EnableRecallHotkey && !_hotkeyService.IsRegistered)
                    {
                        _hotkeyService.Start();
                    }
                    else if (!cfg.EnableRecallHotkey && _hotkeyService.IsRegistered)
                    {
                        _hotkeyService.Stop();
                    }
                }
            };

            _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
            _clipboardMonitor.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Initialization error: {ex.Message}", "ClipFlyout Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task CheckForAutomaticUpdateAsync()
    {
        try
        {
            // Let startup and clipboard monitoring become responsive first.
            await Task.Delay(TimeSpan.FromSeconds(8));
            if (_settingsService?.Current.AutomaticallyInstallUpdates != true) return;
            _settingsService?.UpdateSettings(s => s.LastUpdateCheckUtc = DateTimeOffset.UtcNow);
            var update = await UpdateService.Instance.CheckForUpdateAsync();
            if (update is not null && _settingsService?.Current.AutomaticallyInstallUpdates == true)
            {
                await UpdateService.Instance.DownloadAndStartInstallerAsync(update);
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Automatic update check failed: {ex.Message}");
        }
    }

    private static bool ShouldCheckForUpdates(Models.AppSettings settings)
    {
        return settings.AutomaticallyInstallUpdates &&
               (!settings.LastUpdateCheckUtc.HasValue ||
                DateTimeOffset.UtcNow - settings.LastUpdateCheckUtc.Value >= TimeSpan.FromHours(12));
    }

    private void OnClipboardChanged(object? sender, object rawData)
    {
        if (_detector == null || _windowManager == null || _settingsService == null) return;
        if (!_settingsService.Current.IsMonitoringEnabled) return;

        // Detection may parse JSON, Base64, or large table data. Keep that work
        // off the dispatcher and only present the newest clipboard result.
        long generation = Interlocked.Increment(ref _detectionGeneration);
        var detector = _detector;
        var windowManager = _windowManager;

        _ = Task.Run(() =>
        {
            try
            {
                return detector.Detect(rawData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing clipboard data: {ex}");
                return null;
            }
        }).ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null &&
                generation == Volatile.Read(ref _detectionGeneration) &&
                _settingsService?.Current.IsMonitoringEnabled == true)
            {
                windowManager.ShowFlyout(task.Result);
                _taskbarWidget?.UpdateClipContent(task.Result);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _taskbarWidget?.Close();
        _clipboardMonitor?.Dispose();
        _windowManager?.Dispose();
        _trayIconService?.Dispose();
        _themeService?.Dispose();

        base.OnExit(e);
    }
}
