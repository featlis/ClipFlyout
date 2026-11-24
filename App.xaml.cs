using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClipFlyout.Controls;
using ClipFlyout.Models;
using ClipFlyout.Services;
using ClipFlyout.Views;
using WpfApplication = System.Windows.Application;

namespace ClipFlyout;

public partial class App : WpfApplication
{
    private const string SingleInstanceMutexName = @"Local\ClipFlyout_SingleInstance_Mutex_v2";
    private const string SingleInstanceSignalName = @"Local\ClipFlyout_SingleInstance_Wake_v2";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _wakeSignal;
    private ClipboardMonitor? _clipboardMonitor;
    private ActionExecutor? _actionExecutor;
    private DataTypeDetector? _detector;
    private FlyoutWindowManager? _windowManager;
    private TrayIconService? _trayIconService;
    private SettingsService? _settingsService;
    private ThemeService? _themeService;
    private TaskbarWidgetWindow? _taskbarWidget;
    private HotkeyService? _hotkeyService;
    private DispatcherTimer? _periodicUpdateTimer;
    private readonly CancellationTokenSource _appLifetimeCts = new();
    private long _detectionGeneration;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try { SetCurrentProcessExplicitAppUserModelID("featlis.ClipFlyout"); } catch { }

        // Global unhandled exception safety net
        DispatcherUnhandledException += (_, args) =>
        {
            Debug.WriteLine($"Unhandled UI exception: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Debug.WriteLine($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        // Single-instance guard: signal existing instance and exit cleanly if already running
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingWake = EventWaitHandle.OpenExisting(SingleInstanceSignalName);
                existingWake.Set();
            }
            catch
            {
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            _settingsService = SettingsService.Instance;
            _themeService = ThemeService.Instance;

            _themeService.Mode = _settingsService.Current.Theme;
            LocalizationService.Instance.CurrentLanguage = _settingsService.Current.Language;

            _clipboardMonitor = new ClipboardMonitor
            {
                IsEnabled = _settingsService.Current.IsMonitoringEnabled
            };

            _actionExecutor = new ActionExecutor(_clipboardMonitor);
            _detector = new DataTypeDetector(_actionExecutor);

            // Restore persisted text/pinned history if user opted in
            HistoryService.Instance.LoadPersistedItems(_detector);

            _windowManager = new FlyoutWindowManager(_actionExecutor);
            _windowManager.SettingsRequested += () => _trayIconService?.OpenSettings();
            _windowManager.SetCurrentClipboardFallbackProvider(() =>
            {
                object? currentPayload = _clipboardMonitor?.TryReadCurrentClipboard();
                return currentPayload != null ? _detector?.Detect(currentPayload) : null;
            });

            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyPressed += () => _windowManager?.ToggleOrRecallFlyout();
            if (_settingsService.Current.EnableRecallHotkey)
            {
                _hotkeyService.Start();
            }

            _trayIconService = new TrayIconService(_clipboardMonitor, () => _hotkeyService?.RegistrationFailed == true);
            _trayIconService.BeforeMenuOpen += () => _windowManager?.CaptureExternalForegroundWindow();
            _trayIconService.HistoryItemSelected += result => _windowManager?.ShowFlyout(result, recordHistory: false, interactive: true);
            _trayIconService.SnippetSelected += snippet =>
            {
                _actionExecutor?.CopyText(snippet.Content, "Toast_Copied");
                IntPtr target = _windowManager?.LastExternalForegroundHwnd ?? IntPtr.Zero;
                _actionExecutor?.PasteToActiveWindow(notifyToast: false, targetHwnd: target);
            };
            _trayIconService.RecallRequested += () => _windowManager?.ToggleOrRecallFlyout();

            _taskbarWidget = new TaskbarWidgetWindow();
            _taskbarWidget.FlyoutRequested += result =>
            {
                if (_windowManager == null) return;
                if (_windowManager.IsShowing &&
                    (ReferenceEquals(_windowManager.LastResult, result) ||
                     (_windowManager.LastResult?.Type == result.Type && Equals(_windowManager.LastResult?.RawData, result.RawData))))
                {
                    _windowManager.HideFlyout();
                }
                else
                {
                    _windowManager.ShowFlyout(result, recordHistory: false, interactive: true);
                }
            };
            _taskbarWidget.RecallRequested += () => _windowManager?.ToggleOrRecallFlyout();
            _taskbarWidget.SettingsRequested += () => _trayIconService?.OpenSettings();
            _windowManager.SetTaskbarWidgetHwndProvider(() => _taskbarWidget?.Handle ?? IntPtr.Zero);

            _actionExecutor.ClipboardUpdatedInternally += payload =>
            {
                if (_detector == null || Dispatcher.HasShutdownStarted) return;
                var detector = _detector;
                if (payload is string)
                {
                    _ = Task.Run(() =>
                    {
                        try { return detector.Detect(payload); }
                        catch { return null; }
                    }).ContinueWith(t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                        {
                            _taskbarWidget?.UpdateClipContent(t.Result);
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                else
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var detected = detector.Detect(payload);
                        if (detected != null) _taskbarWidget?.UpdateClipContent(detected);
                    });
                }
            };

            HistoryService.Instance.HistoryChanged += () =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (HistoryService.Instance.Count == 0)
                    {
                        _taskbarWidget?.ClearClipContent();
                        _windowManager?.ClearLastResultIfUnpinned();
                    }
                });
            };

            LocalizationService.Instance.LanguageChanged += () =>
            {
                if (_detector == null || Dispatcher.HasShutdownStarted) return;
                HistoryService.Instance.RefreshLocalization(_detector);
                Dispatcher.BeginInvoke(HydrateInitialWidgetContent);
            };

            if (_settingsService.Current.ShowTaskbarWidget)
            {
                _taskbarWidget.Show();
            }

            // Silently hydrate taskbar widget with current clipboard content on startup
            HydrateInitialWidgetContent();

            // Listen for secondary-instance launch signals -> open Settings
            StartSingleInstanceSignalListener();

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
                _ = CheckForAutomaticUpdateAsync(_appLifetimeCts.Token);
            }

            // Re-check updates periodically for long-running desktop sessions
            _periodicUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(12) };
            _periodicUpdateTimer.Tick += (_, _) =>
            {
                if (_settingsService != null && ShouldCheckForUpdates(_settingsService.Current))
                {
                    _ = CheckForAutomaticUpdateAsync(_appLifetimeCts.Token);
                }
            };
            _periodicUpdateTimer.Start();

            _settingsService.SettingsChanged += cfg =>
            {
                if (_clipboardMonitor != null)
                {
                    _clipboardMonitor.IsEnabled = cfg.IsMonitoringEnabled;
                }
                if (_hotkeyService != null)
                {
                    if (cfg.EnableRecallHotkey)
                    {
                        if (!_hotkeyService.IsRegistered)
                        {
                            _hotkeyService.Start();
                        }
                        else if (!_hotkeyService.MatchesCurrentSettings(cfg))
                        {
                            _hotkeyService.Restart();
                        }
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
            FluentDialog.ShowInfo(null, "ClipFlyout Error", $"Initialization error: {ex.Message}", "OK", "\uE7BA");
            Shutdown();
        }
    }

    private void HydrateInitialWidgetContent()
    {
        try
        {
            object? payload = _clipboardMonitor?.TryReadCurrentClipboard();
            if (payload != null && _detector != null)
            {
                var initial = _detector.Detect(payload);
                if (initial != null)
                {
                    _taskbarWidget?.UpdateClipContent(initial);
                    return;
                }
            }

            var latestHistory = HistoryService.Instance.GetItems();
            if (latestHistory.Count > 0)
            {
                _taskbarWidget?.UpdateClipContent(latestHistory[0].Result);
            }
            else
            {
                _taskbarWidget?.ClearClipContent();
            }
        }
        catch
        {
        }
    }

    private void StartSingleInstanceSignalListener()
    {
        try
        {
            _wakeSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceSignalName);
            var token = _appLifetimeCts.Token;
            _ = Task.Run(() =>
            {
                var handles = new WaitHandle[] { _wakeSignal, token.WaitHandle };
                while (!token.IsCancellationRequested)
                {
                    int signaled = WaitHandle.WaitAny(handles);
                    if (signaled == 0 && !token.IsCancellationRequested)
                    {
                        Dispatcher.BeginInvoke(() => _trayIconService?.OpenSettings());
                    }
                    else
                    {
                        break;
                    }
                }
            }, token);
        }
        catch
        {
        }
    }

    private async Task CheckForAutomaticUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            if (_settingsService?.Current.AutomaticallyInstallUpdates != true) return;
            _settingsService.UpdateSettings(s => s.LastUpdateCheckUtc = DateTimeOffset.UtcNow);

            var update = await UpdateService.Instance.CheckForUpdateAsync(cancellationToken);
            if (update is not null && _windowManager != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var loc = LocalizationService.Instance;
                    var actions = new List<ActionItem>
                    {
                        new(
                            "Action_UpdateNow",
                            loc.Get("Update_Now"),
                            "ArrowDownload24",
                            loc.Get("Update_Now_Desc"),
                            () => _ = InstallUpdateFromFlyoutAsync(update),
                            IsPrimary: true,
                            Subtitle: $"v{update.Version}"
                        ),
                        new(
                            "Action_UpdateDetails",
                            loc.Get("Update_Details"),
                            "Globe24",
                            loc.Get("Update_Details_Desc"),
                            () =>
                            {
                                _windowManager.HideFlyout();
                                string url = update.ReleaseNotesUrl ?? "https://github.com/featlis/ClipFlyout/releases";
                                try { using var _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
                            }
                        )
                    };

                    var updateNotification = new DetectionResult(
                        Type: ClipDataType.PlainText,
                        RawData: $"ClipFlyout v{update.Version}",
                        PreviewTitle: loc.Get("Update_Available_Title"),
                        PreviewSubtitle: $"v{update.Version}",
                        PreviewBody: loc.Get("Update_Available_Body", update.Version),
                        AvailableActions: actions,
                        BadgeText: "UPDATE"
                    );

                    // Never pollute user's clipboard history with update notices!
                    _windowManager.ShowFlyout(updateNotification, recordHistory: false, interactive: true);
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Automatic update check failed: {ex.Message}");
        }
    }

    private async Task InstallUpdateFromFlyoutAsync(UpdateRelease update)
    {
        var loc = LocalizationService.Instance;
        _windowManager?.HideFlyout();
        try
        {
            await UpdateService.Instance.DownloadAndStartInstallerAsync(update, _appLifetimeCts.Token);
            Shutdown();
        }
        catch (Exception ex)
        {
            FluentDialog.ShowInfo(null, "ClipFlyout Update", loc.Get("Update_Error_Dialog", ex.Message), loc.Get("Dialog_OK"), "\uE7BA");
        }
    }

    private static bool ShouldCheckForUpdates(AppSettings settings)
    {
        return settings.AutomaticallyInstallUpdates &&
               (!settings.LastUpdateCheckUtc.HasValue ||
                DateTimeOffset.UtcNow - settings.LastUpdateCheckUtc.Value >= TimeSpan.FromHours(12));
    }

    private void OnClipboardChanged(object? sender, object rawData)
    {
        if (_detector == null || _windowManager == null || _settingsService == null) return;
        if (!_settingsService.Current.IsMonitoringEnabled) return;

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
                Debug.WriteLine($"Error processing clipboard data: {ex}");
                return null;
            }
        }).ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null &&
                generation == Volatile.Read(ref _detectionGeneration) &&
                _settingsService?.Current.IsMonitoringEnabled == true)
            {
                windowManager.ShowFlyout(task.Result, recordHistory: true, interactive: false);
                _taskbarWidget?.UpdateClipContent(task.Result);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _periodicUpdateTimer?.Stop();
        _appLifetimeCts.Cancel();
        _appLifetimeCts.Dispose();
        _hotkeyService?.Dispose();
        _taskbarWidget?.Close();
        _clipboardMonitor?.Dispose();
        _windowManager?.Dispose();
        _trayIconService?.Dispose();
        _themeService?.Dispose();
        _wakeSignal?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
