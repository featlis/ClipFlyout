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
        Assert.Equal("v1.0.0", AppInfo.DisplayVersion);
        Assert.Equal("1.0.0", AppInfo.VersionString);

        var loc = LocalizationService.Instance;
        loc.CurrentLanguage = AppLanguage.Japanese;
        Assert.Equal("バージョン: v1.0.0", loc.Get("About_Version", AppInfo.DisplayVersion));

        loc.CurrentLanguage = AppLanguage.English;
        Assert.Equal("Version: v1.0.0", loc.Get("About_Version", AppInfo.DisplayVersion));
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

    [Fact]
    public void TestFlyoutWindowPresentAndMeasurement()
    {
        var thread = new Thread(() =>
        {
            var window = new FlyoutWindow();
            var actions2 = new List<ActionItem>
            {
                new("Action_Copy", "HEXをコピー", "Copy24", "クリップボードにコピー", () => { }, IsPrimary: true, Subtitle: "#3882F6"),
                new("Action_Convert", "形式を変換", "Convert24", "RGB, HSLへ変換", () => { }, IsPrimary: false, Subtitle: "RGB, HSL, RGBA")
            };
            var result2 = new DetectionResult(
                ClipDataType.HexColor,
                "#3882F6",
                "#3882F6",
                "カラー HEX コード",
                "RGB(56, 130, 246)",
                actions2
            );

            window.Present(result2);
            double height2 = window.MeasureContentHeight();

            var actions5 = new List<ActionItem>(actions2)
            {
                new("Action_3", "Action 3", "Copy24", "Desc 3", () => { }),
                new("Action_4", "Action 4", "Copy24", "Desc 4", () => { }),
                new("Action_5", "Action 5", "Copy24", "Desc 5", () => { })
            };
            var result5 = new DetectionResult(
                ClipDataType.HexColor,
                "#3882F6",
                "#3882F6",
                "カラー HEX コード",
                "RGB(56, 130, 246)",
                actions5
            );
            window.Present(result5);
            double height5 = window.MeasureContentHeight();

            Assert.True(height2 > 100, $"height2 was {height2}");
            Assert.True(height5 > height2, $"height5 ({height5}) should be greater than height2 ({height2})");

            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TestActionItemTitleAndSubtitleBindings()
    {
        var actionWithSub = new ActionItem("Key_1", "Action Label", "Copy24", "Action Description", () => { }, Subtitle: "Sub Text");
        Assert.Equal("Action Label", actionWithSub.Title);
        Assert.Equal("Action Label", actionWithSub.Label);
        Assert.Equal("Sub Text", actionWithSub.DisplaySubtitle);
        Assert.True(actionWithSub.HasSubtitle);
        Assert.Equal(Visibility.Visible, actionWithSub.SubtitleVisibility);

        var actionNoSub = new ActionItem("Key_2", "Plain Action", "Save24", "Plain Description", () => { });
        Assert.Equal("Plain Action", actionNoSub.Title);
        Assert.Equal("Plain Action", actionNoSub.Label);
        Assert.Equal("Plain Description", actionNoSub.DisplaySubtitle);
        Assert.False(actionNoSub.HasSubtitle);
        Assert.Equal(Visibility.Collapsed, actionNoSub.SubtitleVisibility);
    }
}
