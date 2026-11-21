using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

[Collection("SequentialSingleton")]
public class V2OverhaulTests
{
    private sealed class StubClipboardMonitor : IClipboardMonitor
    {
        public event EventHandler<object>? ClipboardChanged { add { } remove { } }
        public bool IsEnabled { get; set; } = true;
        public void Start() { }
        public void Stop() { }
        public IDisposable SuppressNotifications() => new DummyDisposable();
        public void Dispose() { }
        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }

    private DataTypeDetector CreateDetector()
    {
        string tmpSettings = Path.Combine(Path.GetTempPath(), $"cf_v2_test_{Guid.NewGuid():N}.json");
        var settings = new SettingsService(tmpSettings, syncStartupRegistry: false);
        var exec = new ActionExecutor(new StubClipboardMonitor(), _ => { });
        return new DataTypeDetector(exec, settings);
    }

    [Fact]
    public async Task UpdateService_MissingChecksums_FailsClosedWithCryptographicException()
    {
        var release = new UpdateRelease(new Version(9, 9, 9), "https://example.invalid/ClipFlyout-Setup-v9.9.9.exe", ChecksumsUrl: null);
        await Assert.ThrowsAsync<CryptographicException>(() =>
            UpdateService.Instance.DownloadAndStartInstallerAsync(release, CancellationToken.None));
    }

    [Fact]
    public void AppSettings_Normalize_GuardsNaNAndInfinity()
    {
        var bad = new AppSettings
        {
            OpacityPercent = double.NaN,
            DisplayDurationSeconds = double.PositiveInfinity,
            HoverLeaveDurationSeconds = double.NegativeInfinity,
            WidgetOffsetX = double.NaN
        };

        var normalized = bad.Normalize();
        Assert.True(double.IsFinite(normalized.OpacityPercent));
        Assert.True(double.IsFinite(normalized.DisplayDurationSeconds));
        Assert.True(double.IsFinite(normalized.HoverLeaveDurationSeconds));
        Assert.True(double.IsFinite(normalized.WidgetOffsetX));
        Assert.Equal(85.0, normalized.OpacityPercent);
        Assert.Equal(4.0, normalized.DisplayDurationSeconds);
        Assert.Equal(1.5, normalized.HoverLeaveDurationSeconds);
        Assert.Equal(0.0, normalized.WidgetOffsetX);
    }

    [Fact]
    public void ClipboardMonitor_MetadataCheck_AllowsCanIncludeHistoryOne_ExcludesZero()
    {
        // Chrome/WinUI set CanIncludeInClipboardHistory = 1 -> MUST NOT exclude
        Assert.False(ClipboardMonitor.ShouldExcludeClipboardByMetadata(
            hasViewerIgnore: false,
            hasExcludeMonitor: false,
            excludeMonitorDword: null,
            hasCanIncludeHistory: true,
            canIncludeHistoryDword: 1));

        // Password managers set CanIncludeInClipboardHistory = 0 -> MUST exclude
        Assert.True(ClipboardMonitor.ShouldExcludeClipboardByMetadata(
            hasViewerIgnore: false,
            hasExcludeMonitor: false,
            excludeMonitorDword: null,
            hasCanIncludeHistory: true,
            canIncludeHistoryDword: 0));

        // Clipboard Viewer Ignore presence -> MUST exclude
        Assert.True(ClipboardMonitor.ShouldExcludeClipboardByMetadata(
            hasViewerIgnore: true,
            hasExcludeMonitor: false,
            excludeMonitorDword: null,
            hasCanIncludeHistory: false,
            canIncludeHistoryDword: null));
    }

    [Fact]
    public void CleanUrlHelper_PreservesPercentEncoding_AndScopesFeatureParam()
    {
        string encodedUrl = "https://example.com/search?q=a%26b%2Fc&utm_source=newsletter&feature=real_api_param";
        Assert.True(CleanUrlHelper.TryCleanUrl(encodedUrl, out string cleaned));
        Assert.Equal("https://example.com/search?q=a%26b%2Fc&feature=real_api_param", cleaned);

        // On YouTube, si & feature SHOULD be stripped
        string ytUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&si=abc123&feature=youtu.be";
        Assert.True(CleanUrlHelper.TryCleanUrl(ytUrl, out string ytCleaned));
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", ytCleaned);
    }

    [Fact]
    public void DataTypeDetector_DetectsFileDropList()
    {
        var detector = CreateDetector();
        var result = detector.Detect(new[] { @"C:\Users\Demo\report.pdf", @"C:\Users\Demo\notes.txt" });

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.FileDrop, result.Type);
        Assert.Contains("report.pdf", result.PreviewTitle);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyFilePaths" && a.IsPrimary);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyQuotedPaths");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_OpenFolder");
    }

    [Fact]
    public void DataTypeDetector_DetectsCssRgbAndHslColors()
    {
        var detector = CreateDetector();

        var rgb = detector.Detect("rgb(59, 130, 246)");
        Assert.NotNull(rgb);
        Assert.Equal(ClipDataType.HexColor, rgb.Type);
        Assert.Equal("#3B82F6", rgb.HexColorCode);

        var hsl = detector.Detect("hsl(217, 91%, 60%)");
        Assert.NotNull(hsl);
        Assert.Equal(ClipDataType.HexColor, hsl.Type);
        Assert.NotNull(hsl.ColorValue);
    }

    [Fact]
    public void DataTypeDetector_DetectsIsoDateTime()
    {
        var detector = CreateDetector();
        var result = detector.Detect("2026-11-21T08:00:00Z");

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.IsoDateTime, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyUnixSeconds");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyLocalDate" && a.IsPrimary);
    }

    [Fact]
    public void DataTypeDetector_JwtShowsExpirationStatus()
    {
        var detector = CreateDetector();
        // Header: {"alg":"HS256","typ":"JWT"}
        // Payload: {"sub":"user1","exp":1516239022} (expired in 2018)
        string header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"user1\",\"exp\":1516239022}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string jwt = $"{header}.{payload}.signature";

        LocalizationService.Instance.CurrentLanguage = AppLanguage.English;
        var result = detector.Detect(jwt);
        Assert.NotNull(result);
        Assert.Contains("Expired", result.PreviewSubtitle);
    }

    [Fact]
    public void DataTypeDetector_FalsePositiveResistance()
    {
        var detector = CreateDetector();

        // URL with spaces must NOT be classified as URL
        var notUrl = detector.Detect("https://example.com is a nice site");
        Assert.NotNull(notUrl);
        Assert.NotEqual(ClipDataType.Url, notUrl.Type);

        // Two-line prose with comma must NOT be classified as CSV Table
        var notCsv = detector.Detect("Dear Alice,\nThanks, Bob");
        Assert.NotNull(notCsv);
        Assert.NotEqual(ClipDataType.TableData, notCsv.Type);

        // English prose with "public" and "class" must NOT be classified as Code
        var notCode = detector.Detect("The public class starts tomorrow morning.");
        Assert.NotNull(notCode);
        Assert.NotEqual(ClipDataType.Code, notCode.Type);

        // ISBN-13 barcode must NOT be classified as Unix Timestamp
        var isbn = detector.Detect("9780134685991");
        Assert.NotNull(isbn);
        Assert.NotEqual(ClipDataType.UnixTimestamp, isbn.Type);
    }

    [Fact]
    public void DataTypeDetector_ShortRiffBase64_DoesNotThrowOob()
    {
        var detector = CreateDetector();
        // 9-byte RIFF payload (previously triggered IndexOutOfRangeException on bytes[8..11])
        string riffB64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("RIFF12345"));
        var ex = Record.Exception(() => detector.Detect(riffB64));
        Assert.Null(ex);
    }

    [Fact]
    public void DataTypeDetector_HugeText_HandlesFastWithoutCrash()
    {
        var detector = CreateDetector();
        string huge = new string('a', 1_500_000);
        var result = detector.Detect(huge);
        Assert.NotNull(result);
        Assert.Equal(ClipDataType.PlainText, result.Type);
        Assert.True(result.PreviewBody.Length <= 200);
    }

    [Fact]
    public void HistoryService_PinSearchClearAndCleanImageSnippet()
    {
        var history = new HistoryService(Path.Combine(Path.GetTempPath(), $"cf_hist_{Guid.NewGuid():N}.json"));

        var textRes = new DetectionResult(ClipDataType.PlainText, "important api key note", "Plain Text", "sub", "important api key note", []);
        var otherRes = new DetectionResult(ClipDataType.PlainText, "temporary scratchpad", "Plain Text", "sub", "temporary scratchpad", []);

        byte[] px = [0, 128, 255, 255];
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, px, 4);
        var imgRes = new DetectionResult(ClipDataType.Image, bmp, "Image", "1 × 1 px", "", [], ImagePreview: bmp);

        history.Add(textRes);
        history.Add(otherRes);
        history.Add(imgRes);

        // Image snippet must NOT be "System.Windows.Media.Imaging..."
        var imgItem = history.GetItems().First(i => i.Result.Type == ClipDataType.Image);
        Assert.DoesNotContain("Bitmap", imgItem.DisplaySnippet);
        Assert.Contains("1 × 1", imgItem.DisplaySnippet);

        // Pin & Clear unpinned -> pinned item survives!
        Assert.True(history.TogglePin(textRes));
        Assert.True(history.IsPinned(textRes));
        history.Clear(includePinned: false);

        var remaining = history.GetItems();
        Assert.Single(remaining);
        Assert.Equal("important api key note", remaining[0].Result.RawData);

        // Search
        Assert.Single(history.Search("api key"));
        Assert.Empty(history.Search("nonexistent"));
    }

    [Fact]
    public void ThemeService_WcagContrastingForeground_AdaptsToLuminance()
    {
        // Bright yellow/cyan -> dark text
        Assert.Equal(Color.FromRgb(16, 16, 16), ThemeService.GetContrastingForeground(Color.FromRgb(255, 240, 60)));
        Assert.Equal(Color.FromRgb(16, 16, 16), ThemeService.GetContrastingForeground(Color.FromRgb(0, 230, 230)));

        // Deep Windows Blue / Purple -> white text
        Assert.Equal(Color.FromRgb(255, 255, 255), ThemeService.GetContrastingForeground(Color.FromRgb(0, 120, 212)));
        Assert.Equal(Color.FromRgb(255, 255, 255), ThemeService.GetContrastingForeground(Color.FromRgb(116, 77, 169)));
    }

    [Fact]
    public void SettingsService_SettingsChanged_InvokedOutsideLock_NoDeadlock()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"cf_lock_{Guid.NewGuid():N}.json");
        try
        {
            var svc = new SettingsService(tempPath, syncStartupRegistry: false);
            bool reenteredFromOtherThread = false;

            svc.SettingsChanged += _ =>
            {
                // If SettingsChanged were still invoked inside lock(_lock), calling SaveSettings from another thread would deadlock!
                var task = Task.Run(() =>
                {
                    var snapshot = svc.Current;
                    reenteredFromOtherThread = snapshot.OpacityPercent == 77;
                });
                Assert.True(task.Wait(TimeSpan.FromSeconds(2)));
            };

            svc.UpdateSettings(s => s.OpacityPercent = 77);
            Assert.True(reenteredFromOtherThread);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
