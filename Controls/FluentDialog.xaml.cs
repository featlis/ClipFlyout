using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ClipFlyout.Native;
using ClipFlyout.Services;

namespace ClipFlyout.Controls;

public partial class FluentDialog : Window
{
    public bool Confirmed { get; private set; }

    public FluentDialog(string title, string message, string primaryText, string? secondaryText = null, string iconGlyph = "\uE946")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        IconGlyphText.Text = iconGlyph;
        PrimaryButton.Content = primaryText;

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            Activate();
            PrimaryButton.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Confirmed = false;
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SecondaryButton.Visibility != Visibility.Visible)
            {
                Confirmed = true;
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        Win32.EnableMica(hwnd, ThemeService.Instance.IsDarkTheme);
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    public static bool ShowConfirm(Window? owner, string title, string message, string primaryText, string secondaryText, string iconGlyph = "\uE7BA")
    {
        var dlg = new FluentDialog(title, message, primaryText, secondaryText, iconGlyph);
        ConfigureOwnerOrTopmost(dlg, owner);
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    public static void ShowInfo(Window? owner, string title, string message, string okText = "OK", string iconGlyph = "\uE946")
    {
        var dlg = new FluentDialog(title, message, okText, null, iconGlyph);
        ConfigureOwnerOrTopmost(dlg, owner);
        dlg.ShowDialog();
    }

    private static void ConfigureOwnerOrTopmost(FluentDialog dlg, Window? owner)
    {
        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.ShowInTaskbar = true;
            dlg.Topmost = true;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
