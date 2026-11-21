using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using H.NotifyIcon;
using ClipFlyout.Controls;
using ClipFlyout.Models;
using ClipFlyout.Views;
using WpfApplication = System.Windows.Application;

namespace ClipFlyout.Services;

public class TrayIconService : IDisposable
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly TaskbarIcon _taskbarIcon;
    private readonly ContextMenu _contextMenu;
    private readonly Icon _appIcon;
    private readonly HwndSource _anchorSource;
    private readonly Func<bool>? _hotkeyFailedProvider;
    private SettingsWindow? _settingsWindow;
    private bool _isCheckingUpdates;

    private readonly Action<AppSettings> _settingsChangedHandler;
    private readonly Action _languageChangedHandler;

    public static bool IsContextMenuActive { get; private set; }

    public event Action<DetectionResult>? HistoryItemSelected;
    public event Action? RecallRequested;

    public TrayIconService(IClipboardMonitor clipboardMonitor, Func<bool>? hotkeyFailedProvider = null)
    {
        _clipboardMonitor = clipboardMonitor;
        _hotkeyFailedProvider = hotkeyFailedProvider;
        _appIcon = AppIconHelper.CreateAppIcon(32);

        _anchorSource = new HwndSource(new HwndSourceParameters("ClipFlyoutTrayAnchor")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });

        _contextMenu = CreateContextMenu();

        _taskbarIcon = new TaskbarIcon
        {
            Icon = _appIcon,
            ToolTipText = "ClipFlyout"
        };

        // Clean Windows 11 tray UX:
        // Left-click -> toggle/recall flyout; Right-click -> Fluent menu; Double-click -> Settings
        _taskbarIcon.TrayLeftMouseUp += (_, _) => RecallRequested?.Invoke();
        _taskbarIcon.TrayRightMouseUp += (_, _) => ToggleContextMenu();
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => OpenSettings();
        _taskbarIcon.ForceCreate();

        _settingsChangedHandler = _ => UpdateToolTip();
        _languageChangedHandler = UpdateToolTip;

        _settings.SettingsChanged += _settingsChangedHandler;
        _loc.LanguageChanged += _languageChangedHandler;
        UpdateToolTip();
    }

    private void UpdateToolTip()
    {
        var app = WpfApplication.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted) return;

        void Apply()
        {
            bool enabled = _settings.Current.IsMonitoringEnabled;
            _taskbarIcon.ToolTipText = enabled ? _loc.Get("Tray_TitleActive") : _loc.Get("Tray_TitlePaused");
        }

        if (app.Dispatcher.CheckAccess()) Apply();
        else app.Dispatcher.BeginInvoke(Apply);
    }

    public void OpenSettings()
    {
        var app = WpfApplication.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted) return;

        if (app.Dispatcher.CheckAccess()) ShowSettingsInternal();
        else app.Dispatcher.Invoke(ShowSettingsInternal);
    }

    private void ShowSettingsInternal()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_hotkeyFailedProvider);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
        }
    }

    private void ToggleContextMenu()
    {
        if (_contextMenu.IsOpen)
        {
            _contextMenu.IsOpen = false;
            return;
        }

        // Lazy build right before display (zero overhead on clipboard copies!)
        PopulateMenuItems();

        IsContextMenuActive = true;
        if (_anchorSource.Handle != IntPtr.Zero)
        {
            Native.Win32.SetForegroundWindow(_anchorSource.Handle);
        }

        _contextMenu.Placement = PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        if (WpfApplication.Current?.TryFindResource("FluentContextMenuStyle") is Style cmStyle)
        {
            menu.Style = cmStyle;
        }

        menu.Opened += (_, _) => IsContextMenuActive = true;
        menu.Closed += (_, _) =>
        {
            IsContextMenuActive = false;
            if (_anchorSource.Handle != IntPtr.Zero)
            {
                Native.Win32.PostMessage(_anchorSource.Handle, Native.Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
        };

        return menu;
    }

    private MenuItem MakeItem(string header, Action? onClick = null, bool isCheckable = false, bool isChecked = false, bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = isCheckable,
            IsChecked = isChecked,
            IsEnabled = isEnabled
        };

        if (WpfApplication.Current?.TryFindResource("FluentMenuItemStyle") is Style itemStyle)
        {
            item.Style = itemStyle;
        }

        if (onClick != null)
        {
            item.Click += (_, _) => onClick();
        }

        return item;
    }

    private Separator MakeSeparator()
    {
        var sep = new Separator();
        if (WpfApplication.Current?.TryFindResource("FluentMenuSeparatorStyle") is Style sepStyle)
        {
            sep.Style = sepStyle;
        }
        return sep;
    }

    private void PopulateMenuItems()
    {
        _contextMenu.Items.Clear();

        var cfg = _settings.Current;

        // Open Flyout
        _contextMenu.Items.Add(MakeItem(_loc.Get("Tray_Settings_Flyout"), () => RecallRequested?.Invoke()));

        // Recent History Submenu
        var historyMenu = MakeItem(_loc.Get("Tray_History"));
        var items = HistoryService.Instance.GetItems();
        if (items.Count == 0)
        {
            historyMenu.Items.Add(MakeItem(_loc.Get("History_Empty"), isEnabled: false));
        }
        else
        {
            int shown = 0;
            foreach (var h in items)
            {
                if (shown >= 10) break;
                string prefix = h.IsPinned ? "★ " : string.Empty;
                string label = $"{prefix}{h.DisplaySnippet}";
                if (label.Length > 42) label = label[..42] + "…";
                var captured = h.Result;
                historyMenu.Items.Add(MakeItem(label, () => HistoryItemSelected?.Invoke(captured)));
                shown++;
            }
            historyMenu.Items.Add(MakeSeparator());
            historyMenu.Items.Add(MakeItem(_loc.Get("Tray_ClearHistory"), () => HistoryService.Instance.Clear(includePinned: false)));
        }
        _contextMenu.Items.Add(historyMenu);

        _contextMenu.Items.Add(MakeSeparator());

        // Toggle Monitoring
        _contextMenu.Items.Add(MakeItem(
            _loc.Get("Tray_ToggleMonitoring"),
            () =>
            {
                bool next = !_settings.Current.IsMonitoringEnabled;
                _clipboardMonitor.IsEnabled = next;
                _settings.UpdateSettings(s => s.IsMonitoringEnabled = next);
            },
            isCheckable: true,
            isChecked: cfg.IsMonitoringEnabled));

        // Theme Submenu
        var themeMenu = MakeItem(_loc.Get("Tray_Theme"));
        themeMenu.Items.Add(MakeItem(_loc.Get("Tray_ThemeSystem"), () => SetTheme(AppThemeMode.System), true, cfg.Theme == AppThemeMode.System));
        themeMenu.Items.Add(MakeItem(_loc.Get("Tray_ThemeLight"), () => SetTheme(AppThemeMode.Light), true, cfg.Theme == AppThemeMode.Light));
        themeMenu.Items.Add(MakeItem(_loc.Get("Tray_ThemeDark"), () => SetTheme(AppThemeMode.Dark), true, cfg.Theme == AppThemeMode.Dark));
        _contextMenu.Items.Add(themeMenu);

        // Language Submenu
        var langMenu = MakeItem(_loc.Get("Tray_Language"));
        langMenu.Items.Add(MakeItem(_loc.Get("Tray_LangAuto"), () => SetLanguage(AppLanguage.Auto), true, cfg.Language == AppLanguage.Auto));
        langMenu.Items.Add(MakeItem(_loc.Get("Tray_LangJapanese"), () => SetLanguage(AppLanguage.Japanese), true, cfg.Language == AppLanguage.Japanese));
        langMenu.Items.Add(MakeItem(_loc.Get("Tray_LangEnglish"), () => SetLanguage(AppLanguage.English), true, cfg.Language == AppLanguage.English));
        _contextMenu.Items.Add(langMenu);

        _contextMenu.Items.Add(MakeSeparator());

        // Settings & Updates
        _contextMenu.Items.Add(MakeItem(_loc.Get("Tray_Settings"), OpenSettings));
        _contextMenu.Items.Add(MakeItem(
            _isCheckingUpdates ? _loc.Get("Update_Checking") : _loc.Get("Tray_CheckUpdates"),
            () => _ = CheckUpdatesFromTrayAsync(),
            isEnabled: !_isCheckingUpdates));

        _contextMenu.Items.Add(MakeSeparator());

        // Exit
        _contextMenu.Items.Add(MakeItem(_loc.Get("Tray_Exit"), () => WpfApplication.Current?.Shutdown()));
    }

    private void SetTheme(AppThemeMode mode)
    {
        _settings.UpdateSettings(s => s.Theme = mode);
        _theme.Mode = mode;
    }

    private void SetLanguage(AppLanguage lang)
    {
        _settings.UpdateSettings(s => s.Language = lang);
        _loc.CurrentLanguage = lang;
    }

    private async Task CheckUpdatesFromTrayAsync()
    {
        if (_isCheckingUpdates) return;
        _isCheckingUpdates = true;
        try
        {
            var update = await UpdateService.Instance.CheckForUpdateAsync(CancellationToken.None);
            if (update == null)
            {
                FluentDialog.ShowInfo(_settingsWindow, "ClipFlyout", _loc.Get("Update_UpToDate"), _loc.Get("Dialog_OK"), "\uE73E");
                return;
            }

            bool install = FluentDialog.ShowConfirm(
                _settingsWindow,
                _loc.Get("Update_Available_Title"),
                _loc.Get("Update_Available_Body", update.Version),
                _loc.Get("Update_InstallNow"),
                _loc.Get("Dialog_Cancel"),
                "\uE896");

            if (install)
            {
                await UpdateService.Instance.DownloadAndStartInstallerAsync(update, CancellationToken.None);
                WpfApplication.Current?.Shutdown();
            }
        }
        catch (Exception ex)
        {
            FluentDialog.ShowInfo(_settingsWindow, "ClipFlyout", _loc.Get("Update_Error_Dialog", ex.Message), _loc.Get("Dialog_OK"), "\uE7BA");
        }
        finally
        {
            _isCheckingUpdates = false;
        }
    }

    public void Dispose()
    {
        _settings.SettingsChanged -= _settingsChangedHandler;
        _loc.LanguageChanged -= _languageChangedHandler;
        _settingsWindow?.Close();
        _taskbarIcon.Dispose();
        _anchorSource.Dispose();
        _appIcon.Dispose();
    }
}
