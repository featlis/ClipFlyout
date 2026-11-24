using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

[Collection("SequentialSingleton")]
public class V210FeaturesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly MockClipboardMonitor _monitor;
    private string? _copiedText;
    private readonly ActionExecutor _executor;
    private readonly DataTypeDetector _detector;

    public V210FeaturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ClipFlyout_v210_Tests_{Guid.NewGuid():N}");
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

    private static void RunOnSta(Action action, int timeoutSeconds = 20)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(timeoutSeconds)), "STA thread timed out.");
        if (captured != null) throw new AggregateException(captured);
    }

    [Fact]
    public void OcrService_NormalizeCjkSpacing_RemovesArtificialCjkSpacesOnly()
    {
        string raw = "今日 は Windows 11 で テスト 2026 年 です。\n改行 も 保持";
        string normalized = OcrService.NormalizeCjkSpacing(raw);

        Assert.Equal("今日は Windows 11 でテスト 2026 年です。\n改行も保持", normalized);
    }

    [Fact]
    public void OcrService_ExtractTextFromRenderedBanner_RecognizesTextWhenEngineAvailable()
    {
        string? recognized = null;
        bool engineAvailable = false;

        RunOnSta(() =>
        {
            engineAvailable = OcrService.IsAvailable();
            if (!engineAvailable) return;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 560, 140));
                var ft = new FormattedText(
                    "CLIPFLYOUT 2026",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    42,
                    Brushes.Black,
                    1.0);
                dc.DrawText(ft, new Point(32, 40));
            }

            var rtb = new RenderTargetBitmap(560, 140, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            recognized = OcrService.ExtractTextAsync(rtb).GetAwaiter().GetResult();
        });

        if (engineAvailable)
        {
            Assert.False(string.IsNullOrWhiteSpace(recognized));
            Assert.True(
                recognized!.Contains("CLIPFLYOUT", StringComparison.OrdinalIgnoreCase) ||
                recognized.Contains("2026", StringComparison.Ordinal),
                $"Expected OCR output to include rendered text, got: '{recognized}'");
        }
    }

    [Fact]
    public void DataTypeDetector_ImageOcrAction_FollowsEnableOcrSetting()
    {
        var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 16 * 4);
        bmp.Freeze();

        _settings.UpdateSettings(s => s.EnableOcr = true);
        var enabledRes = _detector.Detect(bmp);
        Assert.NotNull(enabledRes);
        Assert.Contains(enabledRes.AvailableActions, a => a.LabelKey == "Action_ExtractOcr");

        _settings.UpdateSettings(s => s.EnableOcr = false);
        var disabledRes = _detector.Detect(bmp);
        Assert.NotNull(disabledRes);
        Assert.DoesNotContain(disabledRes.AvailableActions, a => a.LabelKey == "Action_ExtractOcr");
    }

    [Theory]
    [InlineData(@"C:\Users\demo\project\file.cs", "/mnt/c/Users/demo/project/file.cs", "C:/Users/demo/project/file.cs")]
    [InlineData("D:/Workspace/Repo/src", "/mnt/d/Workspace/Repo/src", "D:/Workspace/Repo/src")]
    [InlineData("/mnt/c/Windows/System32", "/mnt/c/Windows/System32", "C:/Windows/System32")]
    public void DeveloperTools_PathParsingAndConversions_WorkAccurately(string input, string expectedWsl, string expectedForward)
    {
        Assert.True(DeveloperToolsHelper.TryParsePath(input, out var info));
        Assert.Equal(expectedWsl, info.WslPath);
        Assert.Equal(expectedForward, info.ForwardSlashPath);

        var detected = _detector.Detect(input);
        Assert.NotNull(detected);
        Assert.Equal(ClipDataType.FilePath, detected.Type);
    }

    [Fact]
    public void DeveloperTools_UncPathAndQuotedExplorerPath_ParseCleanly()
    {
        Assert.True(DeveloperToolsHelper.TryParsePath(@"\\fileserver\share\docs\spec.pdf", out var unc));
        Assert.Equal(DetectedPathKind.Unc, unc.Kind);

        Assert.True(DeveloperToolsHelper.TryParsePath("\"" + _tempDir + "\"", out var quotedDir));
        Assert.Equal(_tempDir, quotedDir.ExistingDirectory);
    }

    [Theory]
    [InlineData("1920 * 1080", "2073600", ClipDataType.MathExpression)]
    [InlineData("(120 + 30) / 3 =", "50", ClipDataType.MathExpression)]
    [InlineData("0xFF + 0b1010", "265", ClipDataType.MathExpression)]
    [InlineData("0xCAFE", "51966", ClipDataType.MathExpression)]
    public void DeveloperTools_SafeMath_EvaluatesExpectedExpressions(string expr, string expectedFormatted, ClipDataType expectedType)
    {
        Assert.True(DeveloperToolsHelper.TryEvaluateMath(expr, out var res));
        Assert.Equal(expectedFormatted, res.FormattedValue);

        var detected = _detector.Detect(expr);
        Assert.NotNull(detected);
        Assert.Equal(expectedType, detected.Type);
        Assert.Equal(expectedFormatted, detected.PreviewTitle);
    }

    [Theory]
    [InlineData("2026-11-24")]
    [InlineData("2026/11/24")]
    [InlineData("11/24")]
    [InlineData("090-1234-5678")]
    [InlineData("+81-90-1234-5678")]
    [InlineData("2.1.0")]
    [InlineData("123-4567")]
    [InlineData("10 / 0")]
    [InlineData("42")]
    public void DeveloperTools_SafeMath_RejectsDatesPhonesVersionsAndDivZero(string input)
    {
        Assert.False(DeveloperToolsHelper.TryEvaluateMath(input, out _));
        var detected = _detector.Detect(input);
        Assert.NotNull(detected);
        Assert.NotEqual(ClipDataType.MathExpression, detected.Type);
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("{550E8400-E29B-41D4-A716-446655440000}")]
    [InlineData("(550e8400-e29b-41d4-a716-446655440000)")]
    public void DeveloperTools_UuidDetector_FormatsAndGeneratesV4V7(string input)
    {
        Assert.True(DeveloperToolsHelper.TryParseUuid(input, out _));
        var detected = _detector.Detect(input);
        Assert.NotNull(detected);
        Assert.Equal(ClipDataType.Uuid, detected.Type);

        var compact = detected.AvailableActions.First(a => a.LabelKey == "Action_CopyUuidCompact");
        compact.ExecuteAction();
        Assert.Equal("550e8400e29b41d4a716446655440000", _copiedText);

        string v4 = DeveloperToolsHelper.NewUuidV4();
        string v7 = DeveloperToolsHelper.NewUuidV7();
        Assert.True(Guid.TryParseExact(v4, "D", out var g4));
        Assert.True(Guid.TryParseExact(v7, "D", out var g7));
        Assert.Equal(4, (g4.ToByteArray()[7] >> 4) & 0x0F);
        Assert.Equal(7, (g7.ToByteArray()[7] >> 4) & 0x0F);
    }

    [Fact]
    public void DeveloperTools_Sha256TextFileAndBase64EncodeAndJoin_WorkAccurately()
    {
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            DeveloperToolsHelper.ComputeUtf8Sha256("hello"));

        string file = Path.Combine(_tempDir, "hash_sample.bin");
        File.WriteAllText(file, "ClipFlyoutHash");
        string? fileHash = DeveloperToolsHelper.ComputeFileSha256(file);
        string expectedFileHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Equal(expectedFileHash, fileHash);

        Assert.Equal("SGVsbG8=", DeveloperToolsHelper.EncodeUtf8Base64("Hello"));
        Assert.Equal("first\nsecond", DeveloperToolsHelper.JoinClips("first", "second"));
    }

    [Fact]
    public void SnippetService_CrudAndPersistence_WorkReliably()
    {
        string path = Path.Combine(_tempDir, "snippets.json");
        var svc1 = new SnippetService(path);

        var created = svc1.AddOrUpdate("署名", "よろしくお願いいたします。\nfeatlis");
        Assert.Equal(1, svc1.Count);
        Assert.Single(svc1.Search("featlis"));

        var updated = svc1.AddOrUpdate("更新署名", "Best regards,\nClipFlyout", created.Id);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(1, svc1.Count);

        var svc2 = new SnippetService(path);
        var loaded = Assert.Single(svc2.GetItems());
        Assert.Equal("更新署名", loaded.Title);
        Assert.Contains("Best regards", loaded.Content);

        Assert.True(svc2.Delete(created.Id));
        Assert.Equal(0, svc2.Count);

        var svc3 = new SnippetService(path);
        Assert.Empty(svc3.GetItems());
    }

    [Fact]
    public void HistoryService_RemoveAndGetSecondLatestText_WorkProperly()
    {
        var hs = new HistoryService(Path.Combine(_tempDir, "hist_v210.json"), () => false);
        var r1 = _detector.Detect("first_line")!;
        var r2 = _detector.Detect("second_line")!;

        hs.Add(r1);
        Thread.Sleep(15);
        hs.Add(r2);

        Assert.Equal("first_line", hs.GetSecondLatestText("second_line"));
        Assert.True(hs.Remove(r1));
        Assert.Null(hs.GetSecondLatestText("second_line"));
        Assert.Single(hs.GetItems());
    }

    [Fact]
    public void ClipboardMonitor_ExclusionsPauseTimerAndHotkeyFormatting_WorkProperly()
    {
        // Custom + Built-in exclusions
        Assert.True(ClipboardMonitor.IsProcessExcluded("1Password", ignorePasswordManagers: true, null));
        Assert.False(ClipboardMonitor.IsProcessExcluded("1Password", ignorePasswordManagers: false, null));
        Assert.True(ClipboardMonitor.IsProcessExcluded("mstsc", ignorePasswordManagers: false, "notepad, mstsc.exe; Code"));
        Assert.True(ClipboardMonitor.IsProcessExcluded("Code.exe", ignorePasswordManagers: false, "notepad, mstsc.exe, code"));
        Assert.False(ClipboardMonitor.IsProcessExcluded("chrome", ignorePasswordManagers: true, "notepad, mstsc.exe"));

        // Pause timer + auto expiration
        using var monitor = new ClipboardMonitor();
        Assert.False(monitor.IsPaused);
        monitor.PauseFor(TimeSpan.FromMinutes(15));
        Assert.True(monitor.IsPaused);
        Assert.NotNull(monitor.PauseUntilUtc);
        monitor.Resume();
        Assert.False(monitor.IsPaused);

        monitor.PauseFor(TimeSpan.FromMilliseconds(40));
        Assert.True(monitor.IsPaused);
        Thread.Sleep(90);
        Assert.False(monitor.IsPaused);

        // Hotkey formatting + normalization
        Assert.Equal("Ctrl+Shift+K", AppSettings.FormatHotkeyDisplay(0x0002 | 0x0004, (uint)'K'));

        var badCfg = new AppSettings { RecallHotkeyModifiers = 0, RecallHotkeyKey = 0 };
        var norm = badCfg.Normalize();
        Assert.Equal(0x0001u | 0x0004u, norm.RecallHotkeyModifiers);
        Assert.Equal(0x43u, norm.RecallHotkeyKey);
    }
}
