using System;
using System.Linq;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

public class MockClipboardMonitor : IClipboardMonitor
{
    public event EventHandler<object>? ClipboardChanged;
    public bool IsEnabled { get; set; } = true;
    public void Start() { }
    public void Stop() { }
    public IDisposable SuppressNotifications() => new DummyDisposable();
    public void Dispose() { }

    public void TriggerClipboard(object data) => ClipboardChanged?.Invoke(this, data);

    private class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

public class DataTypeDetectorTests
{
    private readonly DataTypeDetector _detector;
    private readonly ActionExecutor _executor;
    private readonly MockClipboardMonitor _monitor;

    public DataTypeDetectorTests()
    {
        _monitor = new MockClipboardMonitor();
        _executor = new ActionExecutor(_monitor);
        _detector = new DataTypeDetector(_executor);

        // Reset settings to all enabled for testing
        SettingsService.Instance.SaveSettings(new AppSettings());
    }

    [Theory]
    [InlineData("#3498DB", 52, 152, 219, 255)]
    [InlineData("#09F", 0, 153, 255, 255)]
    [InlineData("#11223344", 17, 34, 51, 68)]
    [InlineData("#ff00aacc", 255, 0, 170, 204)]
    public void Detect_HexColor_ReturnsValidColorType(string hex, byte expectedR, byte expectedG, byte expectedB, byte expectedA)
    {
        var result = _detector.Detect(hex);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.HexColor, result.Type);
        Assert.NotNull(result.ColorValue);
        Assert.Equal(expectedR, result.ColorValue.Value.R);
        Assert.Equal(expectedG, result.ColorValue.Value.G);
        Assert.Equal(expectedB, result.ColorValue.Value.B);
        Assert.Equal(expectedA, result.ColorValue.Value.A);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyRgb");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyHsl");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyRgba");
    }

    [Fact]
    public void Detect_ValidJsonObject_ReturnsJsonType()
    {
        string json = "{\"name\": \"ClipFlyout\", \"version\": 1, \"active\": true}";
        var result = _detector.Detect(json);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Json, result.Type);
        Assert.Contains("JSON", result.BadgeText);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_FormatJson");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_MinifyJson");
    }

    [Fact]
    public void Detect_ValidJsonArray_ReturnsJsonType()
    {
        string json = "[10, 20, 30, 40]";
        var result = _detector.Detect(json);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Json, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_FormatJson");
    }

    [Theory]
    [InlineData("https://github.com/featlis/ClipFlyout")]
    [InlineData("http://localhost:3000/dashboard?query=test")]
    public void Detect_Url_ReturnsUrlType(string url)
    {
        var result = _detector.Detect(url);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Url, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_OpenBrowser");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyQrCode");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyDomain");
    }

    [Fact]
    public void Detect_CodeSnippet_ReturnsCodeType()
    {
        string code = "public class MyService {\n    public async Task<int> GetDataAsync() {\n        return await Task.FromResult(42);\n    }\n}";
        var result = _detector.Detect(code);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Code, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_AdjustIndent");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_EscapeHtml");
    }

    [Fact]
    public void Detect_PlainText_ReturnsPlainTextType()
    {
        string text = "   Hello world! This is a simple test note.   ";
        var result = _detector.Detect(text);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.PlainText, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_TrimWhitespace");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_TextStats");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_UpperCase");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_LowerCase");
    }

    [Fact]
    public void Detect_DisabledFilters_SkipsDetection()
    {
        SettingsService.Instance.UpdateSettings(s =>
        {
            s.DetectHexColor = false;
            s.DetectJson = false;
            s.DetectPlainText = false;
        });

        // Hex color should be skipped and plain text is also disabled -> returns null
        var colorResult = _detector.Detect("#FF5733");
        Assert.Null(colorResult);

        // JSON should be skipped and plain text is also disabled -> returns null
        var jsonResult = _detector.Detect("{\"key\": \"val\"}");
        Assert.Null(jsonResult);

        // Reset
        SettingsService.Instance.SaveSettings(new AppSettings());
    }

    [Fact]
    public void Localization_LanguageSwitching_WorksCorrectly()
    {
        var loc = LocalizationService.Instance;

        loc.CurrentLanguage = AppLanguage.Japanese;
        Assert.True(loc.IsJapanese);
        Assert.Equal("HEX カラー", loc.Get("Type_HexColor"));
        Assert.Equal("整形してコピー", loc.Get("Action_FormatJson"));
        Assert.Equal("QR画像コピー", loc.Get("Action_CopyQrCode"));
        Assert.Equal("ClipFlyout 設定", loc.Get("Settings_Title"));

        loc.CurrentLanguage = AppLanguage.English;
        Assert.False(loc.IsJapanese);
        Assert.Equal("HEX Color", loc.Get("Type_HexColor"));
        Assert.Equal("Format & Copy", loc.Get("Action_FormatJson"));
        Assert.Equal("Copy QR Image", loc.Get("Action_CopyQrCode"));
        Assert.Equal("ClipFlyout Settings", loc.Get("Settings_Title"));
    }

    [Fact]
    public void Settings_Persistence_Works()
    {
        var settings = SettingsService.Instance;
        settings.UpdateSettings(s =>
        {
            s.DisplayDurationSeconds = 6.5;
            s.Placement = FlyoutPlacement.TopRight;
        });

        Assert.Equal(6.5, settings.Current.DisplayDurationSeconds);
        Assert.Equal(FlyoutPlacement.TopRight, settings.Current.Placement);

        // Reset
        settings.SaveSettings(new AppSettings());
    }
}
