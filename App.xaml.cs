using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipFlyout.Models;
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

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try { SetCurrentProcessExplicitAppUserModelID("featlis.ClipFlyout"); } catch { }
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

            bool isWelcomeArg = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--welcome", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/setup", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-setup", StringComparison.OrdinalIgnoreCase))
                {
                    isWelcomeArg = true;
                    break;
                }
            }

            if (isWelcomeArg)
            {
                var welcome = new WelcomeWindow();
                welcome.CustomizeRequested += () => _trayIconService?.OpenSettings();
                welcome.Show();
                welcome.Activate();
            }
            else if (_settingsService.Current.IsFirstRun)
            {
                _settingsService.UpdateSettings(s => s.IsFirstRun = false);
            }

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
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (_settingsService?.Current.AutomaticallyInstallUpdates != true) return;
            _settingsService?.UpdateSettings(s => s.LastUpdateCheckUtc = DateTimeOffset.UtcNow);
            var update = await UpdateService.Instance.CheckForUpdateAsync();
            if (update is not null && _windowManager != null)
            {
                Dispatcher.Invoke(() =>
                {
                    var loc = LocalizationService.Instance;
                    var actions = new List<ActionItem>
                    {
                        new(
                            "Action_UpdateNow",
                            loc.Get("Update_Now") != "Update_Now" ? loc.Get("Update_Now") : "今すぐ更新",
                            "ArrowDownload24",
                            loc.Get("Update_Now_Desc") != "Update_Now_Desc" ? loc.Get("Update_Now_Desc") : "更新を適用して再起動します",
                            async () =>
                            {
                                _windowManager.HideFlyout();
                                try
                                {
                                    await UpdateService.Instance.DownloadAndStartInstallerAsync(update);
                                    Shutdown();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"更新のインストールに失敗しました: {ex.Message}", "ClipFlyout Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            },
                            IsPrimary: true
                        ),
                        new(
                            "Action_UpdateDetails",
                            loc.Get("Update_Details") != "Update_Details" ? loc.Get("Update_Details") : "詳細",
                            "Globe24",
                            loc.Get("Update_Details_Desc") != "Update_Details_Desc" ? loc.Get("Update_Details_Desc") : "リリースノートを開きます",
                            () =>
                            {
                                _windowManager.HideFlyout();
                                string url = update.ReleaseNotesUrl ?? "https://github.com/featlis/ClipFlyout/releases";
                                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
                            }
                        )
                    };

                    string title = loc.Get("Update_Available_Title") != "Update_Available_Title" ? loc.Get("Update_Available_Title") : "アップデートが利用可能です";
                    string body = $"ClipFlyout v{update.Version} が利用可能です。今すぐ更新できます。";

                    var updateNotification = new DetectionResult(
                        Type: ClipDataType.PlainText,
                        RawData: $"ClipFlyout v{update.Version}",
                        PreviewTitle: title,
                        PreviewSubtitle: $"v{update.Version}",
                        PreviewBody: body,
                        AvailableActions: actions,
                        BadgeText: "UPDATE"
                    );

                    _windowManager.ShowFlyout(updateNotification);
                });
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
