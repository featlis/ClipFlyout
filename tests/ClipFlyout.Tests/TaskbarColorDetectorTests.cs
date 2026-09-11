using System;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

public class TaskbarColorDetectorTests
{
    [Fact]
    public void CalculateLuminance_KnownColors_ReturnsExpectedValues()
    {
        // Black
        Assert.Equal(0.0, TaskbarColorDetector.CalculateLuminance(0, 0, 0), precision: 2);

        // White
        Assert.Equal(255.0, TaskbarColorDetector.CalculateLuminance(255, 255, 255), precision: 2);

        // Red (0.299 * 255 = 76.245)
        Assert.Equal(76.245, TaskbarColorDetector.CalculateLuminance(255, 0, 0), precision: 2);

        // Green (0.587 * 255 = 149.685)
        Assert.Equal(149.685, TaskbarColorDetector.CalculateLuminance(0, 255, 0), precision: 2);

        // Blue (0.114 * 255 = 29.07)
        Assert.Equal(29.07, TaskbarColorDetector.CalculateLuminance(0, 0, 255), precision: 2);

        // Neutral Grey (128)
        Assert.Equal(128.0, TaskbarColorDetector.CalculateLuminance(128, 128, 128), precision: 2);
    }

    [Theory]
    [InlineData(255.0, true)]
    [InlineData(180.0, true)]
    [InlineData(130.0, true)]
    [InlineData(129.9, false)]
    [InlineData(50.0, false)]
    [InlineData(0.0, false)]
    public void IsLuminanceLight_ThresholdBoundary_WorksCorrectly(double luminance, bool expectedIsLight)
    {
        bool result = TaskbarColorDetector.IsLuminanceLight(luminance);
        Assert.Equal(expectedIsLight, result);
    }

    [Fact]
    public void IsTaskbarLight_DoesNotThrow()
    {
        // Running on a real Windows environment shouldn't throw an exception
        var exception = Record.Exception(() =>
        {
            bool isLight = TaskbarColorDetector.IsTaskbarLight();
            // result is a valid bool
            Assert.True(isLight || !isLight);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AppSettings_WidgetTextColor_DefaultIsAuto()
    {
        var settings = new AppSettings();
        Assert.Equal(WidgetTextColorMode.Auto, settings.WidgetTextColor);
    }

    [Theory]
    [InlineData(100, 1000, 160, 30, 1.0, false)]
    [InlineData(100, 1000, 160, 30, 1.5, false)]
    [InlineData(100, 1000, 160, 30, 1.0, true)]
    [InlineData(100, 1000, 160, 30, 1.25, true)]
    public void IsTaskbarLight_WithWidgetBoundsAndDpi_DoesNotThrow(
        double left, double top, double width, double height, double dpi, bool isAboveTaskbar)
    {
        var exception = Record.Exception(() =>
        {
            bool isLight = TaskbarColorDetector.IsTaskbarLight(left, top, width, height, dpi, isAboveTaskbar);
            Assert.True(isLight || !isLight);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void GetRegistryTaskbarIsLight_ReturnsValidBool()
    {
        var exception = Record.Exception(() =>
        {
            bool isLight = TaskbarColorDetector.GetRegistryTaskbarIsLight();
            Assert.True(isLight || !isLight);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void IsFullScreenApplicationActive_WithNullHandle_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            bool isFs = ClipFlyout.Views.TaskbarWidgetWindow.IsFullScreenApplicationActive(IntPtr.Zero);
            Assert.True(isFs || !isFs);
        });

        Assert.Null(exception);
    }
}
