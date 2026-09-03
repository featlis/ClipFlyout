using System;
using System.IO;
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

public class DataTypeDetectorTests : IDisposable
{
    private readonly DataTypeDetector _detector;
    private readonly ActionExecutor _executor;
    private readonly MockClipboardMonitor _monitor;
    private readonly SettingsService _settings;
    private readonly string _settingsDirectory;
    private string? _copiedText;

    public DataTypeDetectorTests()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "ClipFlyout.Tests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(Path.Combine(_settingsDirectory, "settings.json"), syncStartupRegistry: false);
        _monitor = new MockClipboardMonitor();
        _executor = new ActionExecutor(_monitor, text => _copiedText = text);
        _detector = new DataTypeDetector(_executor, _settings);

        // Keep tests isolated from the user's LocalAppData and startup registry.
        _settings.SaveSettings(new AppSettings());
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

    [Fact]
    public void Detect_JsonActions_WorkAfterTheDocumentHasBeenDisposed()
    {
        var result = _detector.Detect("{\"name\":\"ClipFlyout\",\"active\":true}");

        Assert.NotNull(result);
        var formatAction = Assert.Single(result.AvailableActions, action => action.LabelKey == "Action_FormatJson");

        formatAction.ExecuteAction();

        Assert.NotNull(_copiedText);
        Assert.Contains('\n', _copiedText!);
        Assert.Contains("\"name\"", _copiedText!);
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
    public void Detect_CodeIndentAction_NormalizesTheCommonIndentToTwoSpaces()
    {
        const string code = "    public void Run()\n        {\n            return;\n        }";
        var result = _detector.Detect(code);

        Assert.NotNull(result);
        var indentAction = Assert.Single(result.AvailableActions, action => action.LabelKey == "Action_AdjustIndent");

        indentAction.ExecuteAction();

        Assert.Equal($"public void Run(){Environment.NewLine}  {{{Environment.NewLine}    return;{Environment.NewLine}  }}", _copiedText);
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
    public void Detect_UnixTimestamp_Seconds_ReturnsTimestampType()
    {
        // 1725330000 = 2024-09-03
        var result = _detector.Detect("1725330000");

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.UnixTimestamp, result.Type);
        Assert.Contains("Timestamp", result.BadgeText);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyLocalDate");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyIsoDate");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyCurrentTimestamp");
    }

    [Fact]
    public void Detect_UnixTimestamp_Milliseconds_ReturnsTimestampType()
    {
        // 1725330000000 = 13 digits
        var result = _detector.Detect("1725330000000");

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.UnixTimestamp, result.Type);
        Assert.Contains("ms", result.PreviewBody);
    }

    [Fact]
    public void Detect_Base64Text_ReturnsBase64Type()
    {
        // "SGVsbG8gV29ybGQgZnJvbSBDbGlwRmx5b3V0IQ==" = "Hello World from ClipFlyout!"
        string b64 = "SGVsbG8gV29ybGQgZnJvbSBDbGlwRmx5b3V0IQ==";
        var result = _detector.Detect(b64);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Base64, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_DecodeBase64");
    }

    [Fact]
    public void Detect_TableData_Tsv_ReturnsTableType()
    {
        string tsv = "ID\tName\tPrice\n1\tApple\t120\n2\tBanana\t80";
        var result = _detector.Detect(tsv);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.TableData, result.Type);
        Assert.Contains("TSV", result.BadgeText);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToMarkdownTable");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToJsonArray");
    }

    [Fact]
    public void Detect_TableData_Csv_ReturnsTableType()
    {
        string csv = "Title,Author,Year\nClean Code,Martin,2008\nRefactoring,Fowler,1999";
        var result = _detector.Detect(csv);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.TableData, result.Type);
        Assert.Contains("CSV", result.BadgeText);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToMarkdownTable");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToJsonArray");
    }

    [Fact]
    public void Detect_DisabledFilters_SkipsDetection()
    {
        _settings.UpdateSettings(s =>
        {
            s.DetectHexColor = false;
            s.DetectTimestamp = false;
            s.DetectPlainText = false;
        });

        // Hex color should be skipped
        var colorResult = _detector.Detect("#FF5733");
        Assert.Null(colorResult);

        // Timestamp should be skipped
        var tsResult = _detector.Detect("1725330000");
        Assert.Null(tsResult);

        // Reset
        _settings.SaveSettings(new AppSettings());
    }

    [Fact]
    public void Settings_Placement_TopLeft_And_Opacity_Works()
    {
        var settings = _settings;
        settings.UpdateSettings(s =>
        {
            s.Placement = FlyoutPlacement.TopLeft;
            s.OpacityPercent = 75.0;
        });

        Assert.Equal(FlyoutPlacement.TopLeft, settings.Current.Placement);
        Assert.Equal(75.0, settings.Current.OpacityPercent);

        // Reset
        settings.SaveSettings(new AppSettings());
    }

    [Fact]
    public void Detect_CsvWithQuotedCommaAndDuplicateHeaders_ReturnsTableType()
    {
        const string csv = "Name,Name,Note\nAlice,Smith,\"Hello, world\"\nBob,Jones,\"Line one\nLine two\"";

        var result = _detector.Detect(csv);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.TableData, result.Type);
        var jsonAction = Assert.Single(result.AvailableActions, action => action.LabelKey == "Action_ToJsonArray");

        jsonAction.ExecuteAction();

        Assert.Contains("\"Name_2\"", _copiedText!);
        Assert.Contains("Line one\\nLine two", _copiedText!);
    }

    [Fact]
    public void Settings_Normalize_ClampsUntrustedValues()
    {
        var settings = new AppSettings
        {
            OpacityPercent = 150,
            DisplayDurationSeconds = -10,
            HoverLeaveDurationSeconds = 99,
            Placement = (FlyoutPlacement)999
        };

        _settings.SaveSettings(settings);

        Assert.Equal(100, _settings.Current.OpacityPercent);
        Assert.Equal(1.5, _settings.Current.DisplayDurationSeconds);
        Assert.Equal(5, _settings.Current.HoverLeaveDurationSeconds);
        Assert.Equal(FlyoutPlacement.BottomRight, _settings.Current.Placement);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }
}
