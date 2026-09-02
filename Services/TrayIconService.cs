using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using ClipFlyout.Services;
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
    private readonly TaskbarIcon _taskbarIcon;
    private readonly ContextMenu _contextMenu;
    private readonly Icon _appIcon;

    private MenuItem? _toggleItem;
    private MenuItem? _langSubMenu;
    private MenuItem? _langAutoItem;
    private MenuItem? _langJaItem;
    private MenuItem? _langEnItem;
    private MenuItem? _exitItem;

    public TrayIconService(IClipboardMonitor clipboardMonitor)
    {
        _clipboardMonitor = clipboardMonitor;
        _contextMenu = CreateDarkContextMenu();
        _appIcon = CreateAppIcon();

        _taskbarIcon = new TaskbarIcon
        {
            Icon = _appIcon,
            ToolTipText = "ClipFlyout",
            ContextMenu = _contextMenu
        };
        _taskbarIcon.ForceCreate();

        _loc.LanguageChanged += BuildMenu;
        BuildMenu();
    }

    private static ContextMenu CreateDarkContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new WpfBrush(WpfColor.FromRgb(26, 29, 38)),
            Foreground = new WpfBrush(WpfColor.FromRgb(243, 244, 246)),
            BorderBrush = new WpfBrush(WpfColor.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 12.5
        };
        return menu;
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

        // Title header
        var titleItem = new MenuItem
        {
            Header = "ClipFlyout v1.0",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new WpfBrush(WpfColor.FromRgb(156, 163, 175))
        };
        _contextMenu.Items.Add(titleItem);
        _contextMenu.Items.Add(new Separator { Background = new WpfBrush(WpfColor.FromArgb(40, 255, 255, 255)) });

        // Toggle item
        _toggleItem = new MenuItem
        {
            Header = _loc.Get("Tray_ToggleMonitoring"),
            IsCheckable = true,
            IsChecked = _clipboardMonitor.IsEnabled
        };
        _toggleItem.Click += (s, e) =>
        {
            _clipboardMonitor.IsEnabled = !_clipboardMonitor.IsEnabled;
            UpdateStatus();
        };
        _contextMenu.Items.Add(_toggleItem);

        // Language submenu
        _langSubMenu = new MenuItem
        {
            Header = _loc.Get("Tray_Language", "Language")
        };

        _langAutoItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangAuto"),
            IsCheckable = true,
            IsChecked = _loc.CurrentLanguage == AppLanguage.Auto
        };
        _langAutoItem.Click += (s, e) => _loc.CurrentLanguage = AppLanguage.Auto;

        _langJaItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangJapanese"),
            IsCheckable = true,
            IsChecked = _loc.CurrentLanguage == AppLanguage.Japanese
        };
        _langJaItem.Click += (s, e) => _loc.CurrentLanguage = AppLanguage.Japanese;

        _langEnItem = new MenuItem
        {
            Header = _loc.Get("Tray_LangEnglish"),
            IsCheckable = true,
            IsChecked = _loc.CurrentLanguage == AppLanguage.English
        };
        _langEnItem.Click += (s, e) => _loc.CurrentLanguage = AppLanguage.English;

        _langSubMenu.Items.Add(_langAutoItem);
        _langSubMenu.Items.Add(_langJaItem);
        _langSubMenu.Items.Add(_langEnItem);
        _contextMenu.Items.Add(_langSubMenu);

        _contextMenu.Items.Add(new Separator { Background = new WpfBrush(WpfColor.FromArgb(40, 255, 255, 255)) });

        // Exit item
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

                // Rounded background
                using var bgBrush = new LinearGradientBrush(
                    new DrawingRectangle(0, 0, 32, 32),
                    Color.FromArgb(59, 130, 246),
                    Color.FromArgb(147, 51, 234),
                    45f
                );
                using var path = GetRoundedRect(new DrawingRectangle(2, 2, 28, 28), 6);
                g.FillPath(bgBrush, path);

                // Clipboard frame in center
                using var pen = new Pen(Color.White, 2f);
                g.DrawRectangle(pen, 9, 10, 14, 15);
                g.FillRectangle(Brushes.White, 12, 7, 8, 4);

                // Small content lines
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
