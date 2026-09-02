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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _clipboardMonitor = new ClipboardMonitor();
            _actionExecutor = new ActionExecutor(_clipboardMonitor);
            _detector = new DataTypeDetector(_actionExecutor);
            _windowManager = new FlyoutWindowManager(_actionExecutor);
            _trayIconService = new TrayIconService(_clipboardMonitor);

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
        if (_detector == null || _windowManager == null) return;

        try
        {
            var result = _detector.Detect(rawData);
            _windowManager.ShowFlyout(result);
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

        base.OnExit(e);
    }
}
