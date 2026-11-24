using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;
using ClipFlyout.Native;
using ClipFlyout.Services;
using ClipFlyout.Views;
using Xunit;

namespace ClipFlyout.Tests;

public class TaskbarWidgetTests
{
    private static Win32.RECT Rect(int l, int t, int r, int b) =>
        new() { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void IsTrayRectValid_AcceptsStandardWin11Tray()
    {
        var tb = Rect(0, 1032, 1920, 1080);
        var tray = Rect(1599, 1032, 1920, 1080);

        Assert.True(TaskbarWidgetWindow.IsTrayRectValid(tray, tb));
    }

    [Theory]
    [InlineData(0, 1032, 0, 1080)]     // Zero-width hidden/uninitialized tray
    [InlineData(10, 1032, 80, 1080)]   // Bogus far-left rect
    [InlineData(1950, 1032, 2000, 1080)] // Outside taskbar right edge
    public void IsTrayRectValid_RejectsInvalidTrayRects(int l, int t, int r, int b)
    {
        var tb = Rect(0, 1032, 1920, 1080);
        var tray = Rect(l, t, r, b);

        Assert.False(TaskbarWidgetWindow.IsTrayRectValid(tray, tb));
    }

    [Fact]
    public void IsTaskbarOffScreen_DetectsAutoHiddenSliver()
    {
        var mon = Rect(0, 0, 1920, 1080);
        var visibleTb = Rect(0, 1032, 1920, 1080);
        var autoHiddenTb = Rect(0, 1078, 1920, 1126); // 2px visible sliver at bottom

        Assert.False(TaskbarWidgetWindow.IsTaskbarOffScreen(visibleTb, mon));
        Assert.True(TaskbarWidgetWindow.IsTaskbarOffScreen(autoHiddenTb, mon));
    }

    [Fact]
    public void ComputePhysicalBounds_TrayLeft_NeverOverlapsTrayNotifyWnd()
    {
        var mon = Rect(0, 0, 1920, 1080);
        var work = Rect(0, 0, 1920, 1032);
        var tb = Rect(0, 1032, 1920, 1080);
        var tray = Rect(1599, 1032, 1920, 1080);

        var b = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, tray, hasTray: true,
            dpi: 1.0, widgetWidth: 190, widgetHeight: 36,
            WidgetPositionMode.TrayLeft, offsetX: 0);

        Assert.Equal(1599 - 190 - 8, b.PhysX);
        Assert.True(b.PhysX + b.PhysW < tray.Left);
        Assert.Equal(1032 + (48 - 36) / 2, b.PhysY);
        Assert.Equal(190, b.PhysW);
        Assert.Equal(36, b.PhysH);
    }

    [Fact]
    public void ComputePhysicalBounds_SecondaryMonitor_WithoutTaskbar_FloatsAboveBottomRight()
    {
        var mon = Rect(1920, 0, 3840, 1080);
        var work = Rect(1920, 0, 3840, 1080);

        var b = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, default, hasTaskbar: false, default, hasTray: false,
            dpi: 1.0, widgetWidth: 180, widgetHeight: 36,
            WidgetPositionMode.TrayLeft, offsetX: 0);

        Assert.Equal(3840 - 180 - 16, b.PhysX);
        Assert.Equal(1080 - 36 - 10, b.PhysY);
    }

    [Fact]
    public void ComputePhysicalBounds_SecondaryTaskbar_WithoutTray_LeavesSpaceForClock()
    {
        var mon = Rect(1920, 0, 3840, 1080);
        var work = Rect(1920, 0, 3840, 1032);
        var tb = Rect(1920, 1032, 3840, 1080);

        var b = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, default, hasTray: false,
            dpi: 1.0, widgetWidth: 180, widgetHeight: 36,
            WidgetPositionMode.TrayLeft, offsetX: 0);

        Assert.Equal(3840 - 180 - 124, b.PhysX);
        Assert.Equal(1038, b.PhysY);
    }

    [Fact]
    public void ComputePhysicalBounds_HighDpi150_ScalesToPhysicalPixels()
    {
        // 3840x2160 at 150% DPI (1.5) -> 2560x1440 DIP
        var mon = Rect(0, 0, 3840, 2160);
        var work = Rect(0, 0, 3840, 2088);
        var tb = Rect(0, 2088, 3840, 2160);     // 72px physical = 48 DIP
        var tray = Rect(3360, 2088, 3840, 2160); // 480px physical = 320 DIP

        var b = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, tray, hasTray: true,
            dpi: 1.5, widgetWidth: 200, widgetHeight: 36,
            WidgetPositionMode.TrayLeft, offsetX: 0);

        // Expected DIP left: 2240 - 200 - 8 = 2032 -> physical: 3048
        Assert.Equal(3048, b.PhysX);
        Assert.Equal(300, b.PhysW);
        Assert.Equal(54, b.PhysH);
        Assert.True(b.PhysX + b.PhysW < tray.Left);
    }

    [Fact]
    public void ComputePhysicalBounds_LargeOffsetX_ClampsWithinMonitor()
    {
        var mon = Rect(0, 0, 1920, 1080);
        var work = Rect(0, 0, 1920, 1032);
        var tb = Rect(0, 1032, 1920, 1080);

        var rightClamped = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, default, hasTray: false,
            dpi: 1.0, widgetWidth: 180, widgetHeight: 36,
            WidgetPositionMode.TrayLeft, offsetX: 9999);

        var leftClamped = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, default, hasTray: false,
            dpi: 1.0, widgetWidth: 180, widgetHeight: 36,
            WidgetPositionMode.FarLeft, offsetX: -9999);

        Assert.Equal(1920 - 180 - 4, rightClamped.PhysX);
        Assert.Equal(4, leftClamped.PhysX);
    }

    [Theory]
    [InlineData(WidgetPositionMode.FarLeft)]
    [InlineData(WidgetPositionMode.CenterLeft)]
    [InlineData(WidgetPositionMode.CenterRight)]
    [InlineData(WidgetPositionMode.AboveTaskbar)]
    public void ComputePhysicalBounds_AllPositionModes_StayWithinMonitorBounds(WidgetPositionMode mode)
    {
        var mon = Rect(0, 0, 1920, 1080);
        var work = Rect(0, 0, 1920, 1032);
        var tb = Rect(0, 1032, 1920, 1080);
        var tray = Rect(1600, 1032, 1920, 1080);

        var b = TaskbarWidgetWindow.ComputePhysicalBounds(
            mon, work, tb, hasTaskbar: true, tray, hasTray: true,
            dpi: 1.0, widgetWidth: 176, widgetHeight: 36,
            mode, offsetX: 0);

        Assert.InRange(b.PhysX, mon.Left, mon.Right - b.PhysW);
        Assert.InRange(b.PhysY, mon.Top, mon.Bottom - b.PhysH);
        if (mode == WidgetPositionMode.AboveTaskbar)
        {
            Assert.True(b.PhysY + b.PhysH <= work.Bottom);
        }
    }

    [Fact]
    public void TaskbarWidgetWindow_UpdatesAllContentTypesAndStatesWithoutError()
    {
        Exception? threadEx = null;
        var t = new Thread(() =>
        {
            try
            {
                var widget = new TaskbarWidgetWindow();

                // 1. HexColor
                var colorRes = new DetectionResult(
                    ClipDataType.HexColor, "#3B82F6", "#3B82F6", "Color",
                    "rgb(59, 130, 246)", new List<ActionItem>(),
                    ColorValue: Color.FromRgb(59, 130, 246), HexColorCode: "#3B82F6");
                widget.UpdateClipContent(colorRes);
                Assert.InRange(widget.Width, widget.MinWidth, widget.MaxWidth);

                // 2. MathExpression
                var mathRes = new DetectionResult(
                    ClipDataType.MathExpression, "1920 * 1080", "2073600", "1920 * 1080",
                    "1920 * 1080 = 2073600", new List<ActionItem>(), BadgeText: "Math");
                widget.UpdateClipContent(mathRes);
                Assert.InRange(widget.Width, widget.MinWidth, widget.MaxWidth);

                // 3. Image with BitmapSource thumbnail
                var bmp = BitmapSource.Create(
                    16, 16, 96, 96, PixelFormats.Bgr32, null, new byte[16 * 16 * 4], 16 * 4);
                bmp.Freeze();
                var imgRes = new DetectionResult(
                    ClipDataType.Image, bmp, "Image (1920×1080)", "1920 × 1080 px",
                    "1920 × 1080", new List<ActionItem>(), BadgeText: "PNG", ImagePreview: bmp);
                widget.UpdateClipContent(imgRes);
                Assert.InRange(widget.Width, widget.MinWidth, widget.MaxWidth);

                // 4. Paused state toggle + Clear + Theme refresh
                widget.SetPausedState(true);
                widget.SetPausedState(false);
                widget.ClearClipContent();
                widget.ApplyTheme();
                Assert.InRange(widget.Width, widget.MinWidth, widget.MaxWidth);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        Assert.Null(threadEx);
    }
}
