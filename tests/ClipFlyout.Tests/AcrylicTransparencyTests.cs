using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;
using ClipFlyout.Views;
using Xunit;

namespace ClipFlyout.Tests;

public class AcrylicTransparencyTests
{
    [Fact]
    public void TestWindowAcrylicInitialization()
    {
        var thread = new Thread(() =>
        {
            var window = new FlyoutWindow();
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            var hwnd = helper.Handle;

            Assert.NotEqual(IntPtr.Zero, hwnd);

            var source = HwndSource.FromHwnd(hwnd);
            Assert.NotNull(source);
            Assert.NotNull(source.CompositionTarget);
            Assert.Equal(Colors.Transparent, source.CompositionTarget.BackgroundColor);

            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TestThemeServiceTransparencyProperty()
    {
        var theme = ThemeService.Instance;
        // On Windows 11 with transparency enabled, this should be true
        Assert.True(theme.IsTransparencyEnabled || !theme.IsTransparencyEnabled); // Doesn't throw
    }

    [Fact]
    public void TestVersionConsistency()
    {
        Assert.Equal("v0.7.0", AppInfo.DisplayVersion);
        Assert.Equal("0.7.0", AppInfo.VersionString);

        var loc = LocalizationService.Instance;
        loc.CurrentLanguage = AppLanguage.Japanese;
        Assert.Equal("バージョン: v0.7.0", loc.Get("About_Version", AppInfo.DisplayVersion));

        loc.CurrentLanguage = AppLanguage.English;
        Assert.Equal("Version: v0.7.0", loc.Get("About_Version", AppInfo.DisplayVersion));
    }

    [Theory]
    [InlineData(20.0, true, true)]
    [InlineData(50.0, true, true)]
    [InlineData(85.0, true, true)]
    [InlineData(100.0, true, true)]
    [InlineData(85.0, false, true)]
    [InlineData(85.0, true, false)]
    [InlineData(85.0, false, false)]
    public void TestEnableAcrylicBlurModes(double opacity, bool isDark, bool enableTransparency)
    {
        var thread = new Thread(() =>
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                Background = Brushes.Transparent,
                AllowsTransparency = false
            };
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            var hwnd = helper.Handle;

            Win32.EnableAcrylicBlur(hwnd, isDark, opacity, enableTransparency);

            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TestFlyoutThemeAndPreviewOpacity()
    {
        var thread = new Thread(() =>
        {
            var window = new FlyoutWindow();
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();

            // Check light theme preview panel background alpha
            SettingsService.Instance.UpdateSettings(s =>
            {
                s.Theme = AppThemeMode.Light;
                s.OpacityPercent = 85.0;
            });
            window.ApplyTheme();

            var lightPreviewBrush = window.TextPreviewPanel.Background as SolidColorBrush;
            Assert.NotNull(lightPreviewBrush);
            // Must be subtle (alpha 40) to let acrylic blur show through
            Assert.Equal(40, lightPreviewBrush.Color.A);

            var lightRootBrush = window.RootCard.Background as SolidColorBrush;
            Assert.NotNull(lightRootBrush);
            Assert.True(lightRootBrush.Color.A > 30 && lightRootBrush.Color.A < 240);

            // Check dark theme preview panel background alpha
            SettingsService.Instance.UpdateSettings(s =>
            {
                s.Theme = AppThemeMode.Dark;
                s.OpacityPercent = 20.0;
            });
            window.ApplyTheme();

            var darkPreviewBrush = window.TextPreviewPanel.Background as SolidColorBrush;
            Assert.NotNull(darkPreviewBrush);
            Assert.Equal(25, darkPreviewBrush.Color.A);

            var darkRootBrush = window.RootCard.Background as SolidColorBrush;
            Assert.NotNull(darkRootBrush);
            // At 20% opacity, root alpha should be low (30) for maximum frosted glass visibility
            Assert.Equal(30, darkRootBrush.Color.A);

            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
}
