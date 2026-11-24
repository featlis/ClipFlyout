using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

[Collection("SequentialSingleton")]
public class V201BugfixTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly MockClipboardMonitor _monitor;
    private string? _copiedText;
    private readonly ActionExecutor _executor;
    private readonly DataTypeDetector _detector;

    public V201BugfixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ClipFlyout_v201_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settings = new SettingsService(Path.Combine(_tempDir, "settings.json"), syncStartupRegistry: false);
        _settings.SaveSettings(new AppSettings());
        _monitor = new MockClipboardMonitor();
        _executor = new ActionExecutor(_monitor, text => _copiedText = text);
        _detector = new DataTypeDetector(_executor, _settings);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void JsonDetector_CjkCharacters_NotEscapedAsUnicode()
    {
        string rawJson = "{\"greeting\":\"日本語テスト\",\"emoji\":\"クリップボード\"}";
        var result = _detector.Detect(rawJson);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Json, result.Type);
        Assert.Contains("日本語テスト", result.PreviewBody);
        Assert.DoesNotContain("\\u65E5", result.PreviewBody);

        var prettyAction = result.AvailableActions.FirstOrDefault(a => a.LabelKey == "Action_FormatJson");
        Assert.NotNull(prettyAction);
        prettyAction.ExecuteAction();
        Assert.NotNull(_copiedText);
        Assert.Contains("日本語テスト", _copiedText);
        Assert.DoesNotContain("\\u65E5", _copiedText);

        var minifyAction = result.AvailableActions.FirstOrDefault(a => a.LabelKey == "Action_MinifyJson");
        Assert.NotNull(minifyAction);
        minifyAction.ExecuteAction();
        Assert.NotNull(_copiedText);
        Assert.Contains("\"greeting\":\"日本語テスト\"", _copiedText);
        Assert.DoesNotContain("\\u65E5", _copiedText);
    }

    [Fact]
    public void IsoDateTime_WithNanoseconds_DetectsSuccessfully()
    {
        var result = _detector.Detect("2026-11-21T08:00:00.123456789Z");

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.IsoDateTime, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyUnixSeconds");
    }

    [Theory]
    [InlineData("rgb(255 128 0 / 50%)", 255, 128, 0)]
    [InlineData("rgb(12 34 56)", 12, 34, 56)]
    [InlineData("hsl(210 100% 50% / 0.8)", 0, 128, 255)]
    public void ColorDetector_CssColor4SpaceAndSlashSyntax_DetectsColor(string css, byte r, byte g, byte b)
    {
        var result = _detector.Detect(css);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.HexColor, result.Type);
        Assert.NotNull(result.ColorValue);
        Assert.InRange(result.ColorValue.Value.R, (byte)Math.Max(0, r - 2), (byte)Math.Min(255, r + 2));
        Assert.InRange(result.ColorValue.Value.G, (byte)Math.Max(0, g - 2), (byte)Math.Min(255, g + 2));
        Assert.InRange(result.ColorValue.Value.B, (byte)Math.Max(0, b - 2), (byte)Math.Min(255, b + 2));
    }

    [Fact]
    public void Base64Detector_UnpaddedJapaneseUtf8_DetectsAndDecodes()
    {
        const string original = "こんにちはClipFlyout世界テスト";
        string unpadded = Convert.ToBase64String(Encoding.UTF8.GetBytes(original)).TrimEnd('=');

        var result = _detector.Detect(unpadded);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Base64, result.Type);

        var decodeAction = result.AvailableActions.FirstOrDefault(a => a.LabelKey == "Action_DecodeBase64");
        Assert.NotNull(decodeAction);
        decodeAction.ExecuteAction();
        Assert.Equal(original, _copiedText);
    }

    [Fact]
    public void HistoryService_DeterministicSortReloadAndUnpinOrder()
    {
        string historyFile = Path.Combine(_tempDir, "history_sort.json");
        var first = new HistoryService(historyFile, () => true);

        var resA = _detector.Detect("alpha_item")!;
        var resB = _detector.Detect("bravo_item")!;
        var resC = _detector.Detect("charlie_item")!;

        first.Add(resA);
        System.Threading.Thread.Sleep(15);
        first.Add(resB);
        System.Threading.Thread.Sleep(15);
        first.Add(resC);

        // Initial order: newest first -> C, B, A
        Assert.Equal(["charlie_item", "bravo_item", "alpha_item"],
            first.GetItems().Select(i => (string)i.Result.RawData).ToArray());

        // Pin oldest (A) -> moves to top: A, C, B
        Assert.True(first.TogglePin(resA));
        Assert.Equal(["alpha_item", "charlie_item", "bravo_item"],
            first.GetItems().Select(i => (string)i.Result.RawData).ToArray());

        // Unpin (A) -> returns to timestamp order: C, B, A
        Assert.False(first.TogglePin(resA));
        Assert.Equal(["charlie_item", "bravo_item", "alpha_item"],
            first.GetItems().Select(i => (string)i.Result.RawData).ToArray());

        // Pin B and reload into a fresh HistoryService -> must NOT reverse order!
        Assert.True(first.TogglePin(resB));
        first.FlushPersistence();

        var reloaded = new HistoryService(historyFile, () => true);
        reloaded.LoadPersistedItems(_detector);
        var items = reloaded.GetItems();
        Assert.Equal(["bravo_item", "charlie_item", "alpha_item"],
            items.Select(i => (string)i.Result.RawData).ToArray());
        Assert.True(items[0].IsPinned);
        Assert.False(items[1].IsPinned);
    }

    [Fact]
    public void HistoryService_PersistsAndReloadsFileDropItems()
    {
        string historyFile = Path.Combine(_tempDir, "history_files.json");
        string f1 = Path.Combine(_tempDir, "doc1.txt");
        string f2 = Path.Combine(_tempDir, "doc2.txt");
        File.WriteAllText(f1, "one");
        File.WriteAllText(f2, "two");

        var hs1 = new HistoryService(historyFile, () => true);
        var fileRes = _detector.Detect(new[] { f1, f2 });
        Assert.NotNull(fileRes);
        Assert.Equal(ClipDataType.FileDrop, fileRes.Type);

        hs1.Add(fileRes);
        hs1.FlushPersistence();

        var hs2 = new HistoryService(historyFile, () => true);
        hs2.LoadPersistedItems(_detector);

        var items = hs2.GetItems();
        Assert.Single(items);
        Assert.Equal(ClipDataType.FileDrop, items[0].Result.Type);
        var loadedPaths = Assert.IsType<string[]>(items[0].Result.RawData);
        Assert.Equal([f1, f2], loadedPaths);
    }

    [Fact]
    public void Localization_DetectorSubtitlesAndRefreshLocalization_FollowSelectedLanguage()
    {
        var loc = LocalizationService.Instance;
        var origLang = loc.CurrentLanguage;
        string historyFile = Path.Combine(_tempDir, "history_loc.json");
        try
        {
            loc.CurrentLanguage = AppLanguage.Japanese;
            var jaJson = _detector.Detect("{\"k\":1}")!;
            Assert.Contains("オブジェクト", jaJson.PreviewSubtitle);

            var hs = new HistoryService(historyFile, () => false);
            hs.Add(jaJson);

            loc.CurrentLanguage = AppLanguage.English;
            var enJson = _detector.Detect("{\"k\":1}")!;
            Assert.Contains("Object", enJson.PreviewSubtitle);
            Assert.DoesNotContain("オブジェクト", enJson.PreviewSubtitle);

            hs.RefreshLocalization(_detector);
            var refreshed = hs.GetItems().Single();
            Assert.Contains("Object", refreshed.Result.PreviewSubtitle);
        }
        finally
        {
            loc.CurrentLanguage = origLang;
        }
    }
}
