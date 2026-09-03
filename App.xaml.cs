using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipFlyout.Services;
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

            if (_settingsService.Current.AutomaticallyInstallUpdates)
            {
                _ = CheckForAutomaticUpdateAsync();
            }

            _settingsService.SettingsChanged += cfg =>
            {
                if (_clipboardMonitor != null)
                {
                    _clipboardMonitor.IsEnabled = cfg.IsMonitoringEnabled;
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
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _clipboardMonitor?.Dispose();
        _windowManager?.Dispose();
        _trayIconService?.Dispose();
        _themeService?.Dispose();

        base.OnExit(e);
    }
}
