using System;
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

    private void OnClipboardChanged(object? sender, object rawData)
    {
        if (_detector == null || _windowManager == null || _settingsService == null) return;
        if (!_settingsService.Current.IsMonitoringEnabled) return;

        try
        {
            var result = _detector.Detect(rawData);
            if (result != null)
            {
                _windowManager.ShowFlyout(result);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing clipboard data: {ex}");
        }
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
