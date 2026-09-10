using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using H.NotifyIcon;
using ClipFlyout.Models;
using ClipFlyout.Services;
using ClipFlyout.Views;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.SolidColorBrush;
using DrawingSize = System.Drawing.Size;
using DrawingRectangle = System.Drawing.Rectangle;

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
    private SettingsWindow? _settingsWindow;

    private MenuItem? _settingsItem;
    private MenuItem? _toggleItem;
    private MenuItem? _themeSubMenu;
    private MenuItem? _themeSysItem;
    private MenuItem? _themeLightItem;
    private MenuItem? _themeDarkItem;
    private MenuItem? _langSubMenu;
    private MenuItem? _langAutoItem;
    private MenuItem? _langJaItem;
    private MenuItem? _langEnItem;
    private MenuItem? _exitItem;
    private MenuItem? _checkUpdatesItem;
    private readonly Action _themeChangedHandler;
    private readonly Action _historyChangedHandler;

    public TrayIconService(IClipboardMonitor clipboardMonitor)
    {
        _clipboardMonitor = clipboardMonitor;
        _contextMenu = CreateContextMenu();
        _appIcon = AppIconHelper.CreateAppIcon(32);

        // Hidden anchor window provides a valid foreground HWND so the context menu
        // can be dismissed properly when clicking outside (standard Win32 tray behavior).
        _anchorSource = new HwndSource(new HwndSourceParameters("ClipFlyoutTrayAnchor")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });

        _taskbarIcon = new TaskbarIcon
        {
            Icon = _appIcon,
            ToolTipText = "ClipFlyout"
            // Note: We do NOT assign ContextMenu to TaskbarIcon directly. H.NotifyIcon's
            // built-in right-click handler causes immediate popup dismissal due to focus conflicts.
        };
        _taskbarIcon.TrayLeftMouseUp += (s, e) => ToggleContextMenu();
        _taskbarIcon.TrayRightMouseUp += (s, e) => ToggleContextMenu();
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => OpenSettings();
        _taskbarIcon.ForceCreate();

        _themeChangedHandler = () =>
        {
            var app = WpfApplication.Current;
            if (app != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.Invoke(RebuildContextMenu);
            }
        };
        _historyChangedHandler = () =>
        {
            var app = WpfApplication.Current;
            if (app != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.Invoke(RebuildContextMenu);
            }
        };

        _loc.LanguageChanged += BuildMenu;
        _theme.ThemeChanged += _themeChangedHandler;
        HistoryService.Instance.HistoryChanged += _historyChangedHandler;

        BuildMenu();
    }

    public event Action<DetectionResult>? HistoryItemSelected;

    public void OpenSettings()
    {
        if (WpfApplication.Current.Dispatcher.CheckAccess())
        {
            ShowSettingsInternal();
        }
        else
        {
            WpfApplication.Current.Dispatcher.Invoke(ShowSettingsInternal);
        }
    }

    private void ShowSettingsInternal()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow();
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

        IsContextMenuActive = true;
        if (_anchorSource.Handle != IntPtr.Zero)
        {
            Native.Win32.SetForegroundWindow(_anchorSource.Handle);
        }

        _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    public static bool IsContextMenuActive { get; private set; }

    private bool _pendingMenuRebuild;

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            FontSize = 12.5,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        menu.Opened += (_, _) =>
        {
            IsContextMenuActive = true;
        };

        menu.Closed += (_, _) =>
        {
            IsContextMenuActive = false;
            if (_anchorSource.Handle != IntPtr.Zero)
            {
                Native.Win32.PostMessage(_anchorSource.Handle, Native.Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            if (_pendingMenuRebuild)
            {
                _pendingMenuRebuild = false;
                RebuildContextMenu();
            }
        };

        return menu;
    }

    private void UpdateMenuTheme()
    {
        // User requested: Always use the light mode color palette for the tray icon context menu,
        // even when the application / OS is in dark mode.
        var itemForeground = new WpfBrush(WpfColor.FromRgb(24, 32, 45));
        var hoverBackground = new WpfBrush(WpfColor.FromRgb(220, 232, 248));
        var pressedBackground = new WpfBrush(WpfColor.FromRgb(198, 220, 245));
        var menuBackground = new WpfBrush(WpfColor.FromRgb(255, 255, 255));

        _contextMenu.Background = menuBackground;
        _contextMenu.Foreground = itemForeground;
        _contextMenu.BorderBrush = new WpfBrush(WpfColor.FromRgb(226, 232, 240));
        _contextMenu.BorderThickness = new Thickness(1);

        // Explicit MenuItem style for high contrast, clean hover/press behavior
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, itemForeground));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new WpfBrush(WpfColor.FromArgb(0, 0, 0, 0))));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
        style.Triggers.Add(new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, hoverBackground), new Setter(Control.ForegroundProperty, itemForeground) }
        });
        style.Triggers.Add(new Trigger
        {
            Property = MenuItem.IsPressedProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, pressedBackground) }
        });
        _contextMenu.Resources[typeof(MenuItem)] = style;

        // Ensure nested submenus also use the light system keys
        _contextMenu.Resources[System.Windows.SystemColors.MenuBrushKey] = menuBackground;
        _contextMenu.Resources[System.Windows.SystemColors.MenuTextBrushKey] = itemForeground;
        _contextMenu.Resources[System.Windows.SystemColors.HighlightBrushKey] = hoverBackground;
        _contextMenu.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = itemForeground;
        _contextMenu.Resources[System.Windows.SystemColors.ControlBrushKey] = menuBackground;
        _contextMenu.Resources[System.Windows.SystemColors.ControlTextBrushKey] = itemForeground;
    }

    private void BuildMenu()
    {
        if (WpfApplication.Current.Dispatcher.CheckAccess())
        {
            RebuildContextMenu();
        }
        else
        {
            WpfApplication.Current.Dispatcher.Invoke(RebuildContextMenu);
        }
    }

    private void RebuildContextMenu()
    {
        if (_contextMenu.IsOpen)
        {
            _pendingMenuRebuild = true;
            return;
        }

        _contextMenu.Items.Clear();
        UpdateMenuTheme();

        // 1. Title Header
        var titleItem = new MenuItem
        {
            Header = $"ClipFlyout {AppInfo.DisplayVersion}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new WpfBrush(WpfColor.FromRgb(100, 116, 139))
        };
        _contextMenu.Items.Add(titleItem);

        // 2. Settings Item
        _settingsItem = new MenuItem
        {
            Header = _loc.Get("Tray_Settings"),
            FontWeight = FontWeights.SemiBold
        };
        _settingsItem.Click += (s, e) => OpenSettings();
        _contextMenu.Items.Add(_settingsItem);

        // 2b. History Submenu
        var historySubMenu = new MenuItem
        {
            Header = _loc.Get("Tray_History")
        };
        var historyItems = HistoryService.Instance.GetItems();
        if (historyItems.Count == 0)
        {
            historySubMenu.Items.Add(new MenuItem
            {
                Header = _loc.Get("History_Empty"),
                IsEnabled = false
            });
        }
        else
        {
            foreach (var item in historyItems)
            {
                string header = item.DisplaySnippet;
                if (header.Length > 28) header = header[..28] + "…";
                var historyMenuItem = new MenuItem
                {
                    Header = $"{item.Timestamp:HH:mm}  {header}"
                };
                var targetResult = item.Result;
                historyMenuItem.Click += (_, _) =>
                {
                    HistoryItemSelected?.Invoke(targetResult);
                };
                historySubMenu.Items.Add(historyMenuItem);
            }
        }
        _contextMenu.Items.Add(historySubMenu);

        _contextMenu.Items.Add(new Separator());

        // 3. Toggle Monitoring
        _toggleItem = new MenuItem
        {
            Header = _loc.Get("Tray_ToggleMonitoring"),
            IsCheckable = true,
            IsChecked = _clipboardMonitor.IsEnabled
        };
        _toggleItem.Click += (s, e) =>
        {
            bool newVal = !_clipboardMonitor.IsEnabled;
            _clipboardMonitor.IsEnabled = newVal;
            _settings.UpdateSettings(cfg => cfg.IsMonitoringEnabled = newVal);
            UpdateStatus();
        };
        _contextMenu.Items.Add(_toggleItem);

        // 4. Theme Submenu
        _themeSubMenu = new MenuItem
        {
            Header = _loc.Get("Tray_Theme")
        };

        _themeSysItem = new MenuItem
        {
            Header = _loc.Get("Tray_ThemeSystem"),
            IsCheckable = true,
            IsChecked = _settings.Current.Theme == AppThemeMode.System
        };
        _themeSysItem.Click += (s, e) =>
        {
            _theme.Mode = AppThemeMode.System;
            _settings.UpdateSettings(cfg => cfg.Theme = AppThemeMode.System);
            RebuildContextMenu();
        };

        _themeLightItem = new MenuItem
        {
            Header = _loc.Get("Tray_ThemeLight"),
            IsCheckable = true,
            IsChecked = _settings.Current.Theme == AppThemeMode.Light
        };
        _themeLightItem.Click += (s, e) =>
        {
            _theme.Mode = AppThemeMode.Light;
            _settings.UpdateSettings(cfg => cfg.Theme = AppThemeMode.Light);
            RebuildContextMenu();
        };

        _themeDarkItem = new MenuItem
        {
            Header = _loc.Get("Tray_ThemeDark"),
            IsCheckable = true,
            IsChecked = _settings.Current.Theme == AppThemeMode.Dark
        };
        _themeDarkItem.Click += (s, e) =>
        {
            _theme.Mode = AppThemeMode.Dark;
            _settings.UpdateSettings(cfg => cfg.Theme = AppThemeMode.Dark);
            RebuildContextMenu();
        };

        _themeSubMenu.Items.Add(_themeSysItem);
        _themeSubMenu.Items.Add(_themeLightItem);
        _themeSubMenu.Items.Add(_themeDarkItem);
        _contextMenu.Items.Add(_themeSubMenu);

        // 5. Language Submenu
        _langSubMenu = new MenuItem
        {
            Header = _loc.Get("Tray_Language")
        };

        _langAutoItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangAuto"),
            IsCheckable = true,
            IsChecked = _settings.Current.Language == AppLanguage.Auto
        };
        _langAutoItem.Click += (s, e) =>
        {
            _loc.CurrentLanguage = AppLanguage.Auto;
            _settings.UpdateSettings(cfg => cfg.Language = AppLanguage.Auto);
        };

        _langJaItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangJapanese"),
            IsCheckable = true,
            IsChecked = _settings.Current.Language == AppLanguage.Japanese
        };
        _langJaItem.Click += (s, e) =>
        {
            _loc.CurrentLanguage = AppLanguage.Japanese;
            _settings.UpdateSettings(cfg => cfg.Language = AppLanguage.Japanese);
        };

        _langEnItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangEnglish"),
            IsCheckable = true,
            IsChecked = _settings.Current.Language == AppLanguage.English
        };
        _langEnItem.Click += (s, e) =>
        {
            _loc.CurrentLanguage = AppLanguage.English;
            _settings.UpdateSettings(cfg => cfg.Language = AppLanguage.English);
        };

        _langSubMenu.Items.Add(_langAutoItem);
        _langSubMenu.Items.Add(_langJaItem);
        _langSubMenu.Items.Add(_langEnItem);
        _contextMenu.Items.Add(_langSubMenu);

        _contextMenu.Items.Add(new Separator());

        _checkUpdatesItem = new MenuItem { Header = _loc.Get("Tray_CheckUpdates") };
        _checkUpdatesItem.Click += async (_, _) => await CheckForUpdatesAsync();
        _contextMenu.Items.Add(_checkUpdatesItem);

        // 6. Exit
        _exitItem = new MenuItem
        {
            Header = _loc.Get("Tray_Exit")
        };
        _exitItem.Click += (s, e) => WpfApplication.Current.Shutdown();
        _contextMenu.Items.Add(_exitItem);

        UpdateStatus();
    }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        if (_checkUpdatesItem != null) _checkUpdatesItem.IsEnabled = false;
        try
        {
            var result = await UpdateService.Instance.CheckForUpdateAsync();
            if (result is null)
            {
                MessageBox.Show(_loc.Get("Update_UpToDate"), "ClipFlyout", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var choice = MessageBox.Show(
                _loc.Get("Update_Available", result.Version),
                "ClipFlyout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                await UpdateService.Instance.DownloadAndStartInstallerAsync(result);
                WpfApplication.Current.Shutdown();
            }
        }
        catch
        {
            MessageBox.Show(_loc.Get("Update_Failed"), "ClipFlyout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (_checkUpdatesItem != null) _checkUpdatesItem.IsEnabled = true;
        }
    }

    private void UpdateStatus()
    {
        if (_toggleItem != null)
        {
            _toggleItem.IsChecked = _clipboardMonitor.IsEnabled;
        }

        string title = _clipboardMonitor.IsEnabled ? _loc.Get("Tray_TitleActive") : _loc.Get("Tray_TitlePaused");
        _taskbarIcon.ToolTipText = title.Length > 63 ? title[..63] : title;
    }

    public void Dispose()
    {
        _loc.LanguageChanged -= BuildMenu;
        _theme.ThemeChanged -= _themeChangedHandler;
        HistoryService.Instance.HistoryChanged -= _historyChangedHandler;
        _taskbarIcon.Dispose();
        _appIcon.Dispose();
        _anchorSource.Dispose();
    }
}
