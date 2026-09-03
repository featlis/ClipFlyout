using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
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

    public TrayIconService(IClipboardMonitor clipboardMonitor)
    {
        _clipboardMonitor = clipboardMonitor;
        _contextMenu = CreateContextMenu();
        _appIcon = CreateAppIcon();

        _taskbarIcon = new TaskbarIcon
        {
            Icon = _appIcon,
            ToolTipText = "ClipFlyout",
            ContextMenu = _contextMenu
        };
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => OpenSettings();
        _taskbarIcon.ForceCreate();

        _loc.LanguageChanged += BuildMenu;
        _theme.ThemeChanged += () => WpfApplication.Current.Dispatcher.Invoke(UpdateMenuTheme);

        BuildMenu();
    }

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

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            FontSize = 12.5
        };
        return menu;
    }

    private void UpdateMenuTheme()
    {
        bool isDark = _theme.IsDarkTheme;
        if (isDark)
        {
            _contextMenu.Background = new WpfBrush(WpfColor.FromRgb(28, 31, 40));
            _contextMenu.Foreground = new WpfBrush(WpfColor.FromRgb(243, 244, 246));
            _contextMenu.BorderBrush = new WpfBrush(WpfColor.FromArgb(60, 255, 255, 255));
            _contextMenu.BorderThickness = new Thickness(1);
        }
        else
        {
            _contextMenu.Background = new WpfBrush(WpfColor.FromRgb(255, 255, 255));
            _contextMenu.Foreground = new WpfBrush(WpfColor.FromRgb(15, 23, 42));
            _contextMenu.BorderBrush = new WpfBrush(WpfColor.FromRgb(226, 232, 240));
            _contextMenu.BorderThickness = new Thickness(1);
        }
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
        _contextMenu.Items.Clear();
        UpdateMenuTheme();

        // 1. Title Header
        var titleItem = new MenuItem
        {
            Header = "ClipFlyout v0.3.0",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new WpfBrush(_theme.IsDarkTheme ? WpfColor.FromRgb(156, 163, 175) : WpfColor.FromRgb(100, 116, 139))
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

        // 6. Exit
        _exitItem = new MenuItem
        {
            Header = _loc.Get("Tray_Exit")
        };
        _exitItem.Click += (s, e) => WpfApplication.Current.Shutdown();
        _contextMenu.Items.Add(_exitItem);

        UpdateStatus();
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

    private static Icon CreateAppIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var bgBrush = new LinearGradientBrush(
                    new DrawingRectangle(0, 0, 32, 32),
                    Color.FromArgb(59, 130, 246),
                    Color.FromArgb(147, 51, 234),
                    45f
                );
                using var path = GetRoundedRect(new DrawingRectangle(2, 2, 28, 28), 6);
                g.FillPath(bgBrush, path);

                using var pen = new Pen(Color.White, 2f);
                g.DrawRectangle(pen, 9, 10, 14, 15);
                g.FillRectangle(Brushes.White, 12, 7, 8, 4);

                using var linePen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f);
                g.DrawLine(linePen, 12, 14, 20, 14);
                g.DrawLine(linePen, 12, 18, 18, 18);
            }

            IntPtr hIcon = bmp.GetHicon();
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static GraphicsPath GetRoundedRect(DrawingRectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var size = new DrawingSize(diameter, diameter);
        var arc = new DrawingRectangle(bounds.Location, size);
        var path = new GraphicsPath();

        if (radius == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _loc.LanguageChanged -= BuildMenu;
        _taskbarIcon.Dispose();
        _appIcon.Dispose();
    }
}
