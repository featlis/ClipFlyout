using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using ClipFlyout.Services;

namespace ClipFlyout.Services;

public class TrayIconService : IDisposable
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly TaskbarIcon _taskbarIcon;
    private readonly ContextMenu _contextMenu;

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

        _taskbarIcon = new TaskbarIcon
        {
            IconSource = CreateAppIconSource(),
            ToolTipText = "ClipFlyout",
            ContextMenu = _contextMenu
        };

        _loc.LanguageChanged += BuildMenu;
        BuildMenu();
    }

    private static ContextMenu CreateDarkContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 29, 38)),
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 12.5
        };
        return menu;
    }

    private void BuildMenu()
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            RebuildContextMenu();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(RebuildContextMenu);
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
            Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175))
        };
        _contextMenu.Items.Add(titleItem);
        _contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });

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

        _contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });

        // Exit item
        _exitItem = new MenuItem
        {
            Header = _loc.Get("Tray_Exit")
        };
        _exitItem.Click += (s, e) => Application.Current.Shutdown();
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

    private static ImageSource CreateAppIconSource()
    {
        // 32x32 DrawingImage
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Gradient rounded rectangle
            var gradient = new LinearGradientBrush(
                Color.FromRgb(59, 130, 246),
                Color.FromRgb(147, 51, 234),
                new Point(0, 0),
                new Point(1, 1)
            );
            dc.DrawRoundedRectangle(gradient, null, new Rect(2, 2, 28, 28), 6, 6);

            // Center clipboard icon
            var whitePen = new Pen(Brushes.White, 2.0);
            dc.DrawRoundedRectangle(null, whitePen, new Rect(9, 10, 14, 15), 2, 2);
            dc.DrawRectangle(Brushes.White, null, new Rect(12, 7, 8, 4));

            // Lines
            var thinPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.5);
            dc.DrawLine(thinPen, new Point(12, 15), new Point(20, 15));
            dc.DrawLine(thinPen, new Point(12, 19), new Point(18, 19));
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public void Dispose()
    {
        _loc.LanguageChanged -= BuildMenu;
        _taskbarIcon.Dispose();
    }
}
