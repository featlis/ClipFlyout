using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClipFlyout.Models;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

public class ExtendedFeaturesTests : IDisposable
{
    private readonly DataTypeDetector _detector;
    private readonly ActionExecutor _executor;
    private readonly MockClipboardMonitor _monitor;
    private readonly SettingsService _settings;
    private readonly string _settingsDirectory;
    private string? _copiedText;

    public ExtendedFeaturesTests()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "ClipFlyout.ExtendedTests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(Path.Combine(_settingsDirectory, "settings.json"), syncStartupRegistry: false);
        _monitor = new MockClipboardMonitor();
        _executor = new ActionExecutor(_monitor, text => _copiedText = text);
        _detector = new DataTypeDetector(_executor, _settings);

        _settings.SaveSettings(new AppSettings());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void CleanUrl_StripsTrackingParameters()
    {
        string dirty = "https://example.com/page?id=42&utm_source=twitter&utm_medium=social&fbclid=IwAR0123&keep=yes";
        bool result = CleanUrlHelper.TryCleanUrl(dirty, out string clean);

        Assert.True(result);
        Assert.Contains("id=42", clean);
        Assert.Contains("keep=yes", clean);
        Assert.DoesNotContain("utm_source", clean);
        Assert.DoesNotContain("utm_medium", clean);
        Assert.DoesNotContain("fbclid", clean);
    }

    [Fact]
    public void CleanUrl_StripsYouTubeAndAmazonTrackingParameters()
    {
        string yt = "https://youtu.be/abcdef?si=xyz123&t=40";
        bool ytResult = CleanUrlHelper.TryCleanUrl(yt, out string ytClean);
        Assert.True(ytResult);
        Assert.DoesNotContain("si=xyz123", ytClean);
        Assert.Contains("t=40", ytClean);

        string amz = "https://amazon.co.jp/dp/B00000?ref_=cm_sw_r_cp_ud&tag=test";
        bool amzResult = CleanUrlHelper.TryCleanUrl(amz, out string amzClean);
        Assert.True(amzResult);
        Assert.DoesNotContain("ref_=", amzClean);
    }

    [Fact]
    public void CleanUrl_ReturnsFalseWhenNoTrackingParams()
    {
        string cleanInput = "https://example.com/search?q=csharp&sort=newest";
        bool result = CleanUrlHelper.TryCleanUrl(cleanInput, out string output);

        Assert.False(result);
        Assert.Equal(cleanInput, output);
    }

    [Fact]
    public void CleanUrl_ReturnsFalseForInvalidUrl()
    {
        bool result = CleanUrlHelper.TryCleanUrl("not-a-valid-url", out _);
        Assert.False(result);
    }

    [Theory]
    [InlineData("helloWorld", true)]
    [InlineData("hello_world", true)]
    [InlineData("hello-world", true)]
    [InlineData("HelloWorld", true)]
    [InlineData("HELLO_WORLD", true)]
    [InlineData("someVariable123", true)]
    [InlineData("", false)]
    [InlineData("a", false)]
    [InlineData("this is a sentence that has way too many words to be an identifier in any language", false)]
    [InlineData("line1\nline2", false)]
    public void CaseConverter_IsConvertible(string input, bool expected)
    {
        bool actual = CaseConverter.IsConvertible(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CaseConverter_Conversions()
    {
        var words = CaseConverter.SplitWords("userProfileData");

        Assert.Equal("userProfileData", CaseConverter.ToCamelCase(words));
        Assert.Equal("UserProfileData", CaseConverter.ToPascalCase(words));
        Assert.Equal("user_profile_data", CaseConverter.ToSnakeCase(words));
        Assert.Equal("user-profile-data", CaseConverter.ToKebabCase(words));
        Assert.Equal("USER_PROFILE_DATA", CaseConverter.ToConstantCase(words));
    }

    [Fact]
    public void CaseConverter_SnakeToCamel()
    {
        var words = CaseConverter.SplitWords("get_user_account_by_id");
        Assert.Equal("getUserAccountById", CaseConverter.ToCamelCase(words));
        Assert.Equal("GetUserAccountById", CaseConverter.ToPascalCase(words));
    }

    [Fact]
    public void HistoryService_CapsAtTenItems_AndOrdersNewestFirst()
    {
        var service = HistoryService.Instance;
        service.Clear();

        for (int i = 1; i <= 15; i++)
        {
            var result = new DetectionResult(
                ClipDataType.PlainText,
                $"Raw {i}",
                $"Item {i}",
                "Subtitle",
                $"Snippet {i}",
                []
            );
            service.Add(result);
        }

        var items = service.GetItems();
        Assert.Equal(10, items.Count);
        Assert.Equal("Item 15", items[0].DisplayTitle);
        Assert.Equal("Item 6", items[^1].DisplayTitle);
    }

    [Fact]
    public void HistoryService_RemovesDuplicateWhenReAdded()
    {
        var service = HistoryService.Instance;
        service.Clear();

        var result1 = new DetectionResult(ClipDataType.PlainText, "SameData", "Title 1", "Sub 1", "Snip 1", []);
        var result2 = new DetectionResult(ClipDataType.PlainText, "OtherData", "Title 2", "Sub 2", "Snip 2", []);
        var result3 = new DetectionResult(ClipDataType.PlainText, "SameData", "Title 3", "Sub 3", "Snip 3", []);

        service.Add(result1);
        service.Add(result2);
        service.Add(result3);

        var items = service.GetItems();
        Assert.Equal(2, items.Count);
        Assert.Equal("Title 3", items[0].DisplayTitle);
        Assert.Equal("Title 2", items[1].DisplayTitle);
    }

    [Theory]
    [InlineData("1Password", true)]
    [InlineData("bitwarden", true)]
    [InlineData("KeePass", true)]
    [InlineData("keepassxc", true)]
    [InlineData("LastPass", true)]
    [InlineData("dashlane", true)]
    [InlineData("notepad", false)]
    [InlineData("explorer", false)]
    [InlineData("chrome", false)]
    public void SensitiveApp_Detection(string processName, bool expected)
    {
        bool actual = ClipboardMonitor.IsSensitiveProcessName(processName);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DataTypeDetector_DetectsJwtToken()
    {
        // Valid JWT token format (Header.Payload.Signature)
        // Header: {"alg":"HS256","typ":"JWT"} -> eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
        // Payload: {"sub":"1234567890","name":"John Doe","iat":1516239022} -> eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ
        string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var result = _detector.Detect(jwt);

        Assert.NotNull(result);
        Assert.Contains("JWT", result.PreviewTitle);
        Assert.Contains("John Doe", result.PreviewBody);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyJwtPayload");
    }

    [Fact]
    public void DataTypeDetector_GeneratesCleanUrlAction()
    {
        string dirtyUrl = "https://github.com/features?utm_source=adwords&utm_campaign=promo";

        var result = _detector.Detect(dirtyUrl);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.Url, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_CopyCleanUrl");
    }

    [Fact]
    public void DataTypeDetector_GeneratesCaseConverterActions()
    {
        string identifier = "calculate_user_total_score";

        var result = _detector.Detect(identifier);

        Assert.NotNull(result);
        Assert.Equal(ClipDataType.PlainText, result.Type);
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToCamelCase");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToPascalCase");
        Assert.Contains(result.AvailableActions, a => a.LabelKey == "Action_ToKebabCase");
    }

    [Fact]
    public void AppIconHelper_GeneratesValidIcon()
    {
        string tempIco = Path.Combine(Path.GetTempPath(), $"test_icon_{Guid.NewGuid():N}.ico");
        try
        {
            AppIconHelper.SaveMultiResolutionIco(tempIco);
            Assert.True(File.Exists(tempIco));
            var fi = new FileInfo(tempIco);
            Assert.True(fi.Length > 1000);

            // Also ensure assets/app.ico is up-to-date with this high-res multi-icon
            string projectAsset = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "app.ico"));
            if (File.Exists(projectAsset))
            {
                AppIconHelper.SaveMultiResolutionIco(projectAsset);
                string pngAsset = Path.Combine(Path.GetDirectoryName(projectAsset)!, "app-icon.png");
                using var bmp = AppIconHelper.CreateAppBitmap(256);
                bmp.Save(pngAsset, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        finally
        {
            if (File.Exists(tempIco)) File.Delete(tempIco);
        }
    }

    [Fact]
    public void AppSettings_DefaultValues_AlignWithV070Requirements()
    {
        var settings = new AppSettings();
        Assert.Equal(AppThemeMode.System, settings.Theme);
        Assert.True(settings.IsFirstRun);
        Assert.True(settings.ShowTaskbarWidget);
        Assert.Equal(WidgetPositionMode.TrayLeft, settings.WidgetPosition);
        Assert.Equal(0, settings.WidgetOffsetX);
    }

    [Fact]
    public void AppSettings_WidgetOffsetX_ClampsProperly()
    {
        var settings = new AppSettings { WidgetOffsetX = 999 };
        var normalized = settings.Normalize();
        Assert.Equal(800, normalized.WidgetOffsetX);

        settings.WidgetOffsetX = -999;
        normalized = settings.Normalize();
        Assert.Equal(-800, normalized.WidgetOffsetX);
    }

    [Fact]
    public void ActionItem_IsPrimaryProperty_InitializesCorrectly()
    {
        var action = new ActionItem("Action_Test", "Test", "Icon", "Desc", () => { }, IsPrimary: true);
        Assert.True(action.IsPrimary);

        var secondary = new ActionItem("Action_Test2", "Test2", "Icon", "Desc", () => { });
        Assert.False(secondary.IsPrimary);
    }

    [Fact]
    public void AppSettings_Normalize_AcceptsCustomHexColors()
    {
        var settings = new AppSettings { AccentColor = "#10B981" };
        var normalized = settings.Normalize();
        Assert.Equal("#10B981", normalized.AccentColor);

        // Invalid hex fallback
        settings.AccentColor = "invalid";
        normalized = settings.Normalize();
        Assert.Equal("#0078D4", normalized.AccentColor);
    }

    [Fact]
    public void AppSettings_Normalize_PreservesSystemTheme()
    {
        var settings = new AppSettings { Theme = AppThemeMode.System };
        var normalized = settings.Normalize();
        Assert.Equal(AppThemeMode.System, normalized.Theme);
    }

    [Fact]
    public void LocalizationService_CreditsLicense_UsesOfficialGPLv3Format()
    {
        var loc = LocalizationService.Instance;
        loc.CurrentLanguage = AppLanguage.Japanese;
        string jaLicense = loc.Get("Credits_License");
        Assert.Contains("GNU General Public License version 3", jaLicense);
        Assert.Contains("GPL-3.0-or-later", jaLicense);

        loc.CurrentLanguage = AppLanguage.English;
        string enLicense = loc.Get("Credits_License");
        Assert.Contains("GNU General Public License version 3", enLicense);
        Assert.Contains("GPL-3.0-or-later", enLicense);
    }

    [Fact]
    public void CleanUrl_PreservesLegitimateRefAndSource()
    {
        string legitimate = "https://example.com/repo?source=feed&ref=main";
        bool result = CleanUrlHelper.TryCleanUrl(legitimate, out string output);
        Assert.False(result);
        Assert.Equal(legitimate, output);

        string mixed = "https://example.com/repo?ref=main&utm_source=twitter&ref_src=twsrc";
        bool mixedResult = CleanUrlHelper.TryCleanUrl(mixed, out string mixedClean);
        Assert.True(mixedResult);
        Assert.Contains("ref=main", mixedClean);
        Assert.DoesNotContain("utm_source", mixedClean);
        Assert.DoesNotContain("ref_src", mixedClean);
    }

    [Fact]
    public void AppSettings_Normalize_ValidatesWidgetTextColor()
    {
        var settings = new AppSettings { WidgetTextColor = (WidgetTextColorMode)999 };
        var normalized = settings.Normalize();
        Assert.Equal(WidgetTextColorMode.Auto, normalized.WidgetTextColor);
    }

    [Fact]
    public void HistoryService_DeduplicatesIdenticalImages()
    {
        var history = HistoryService.Instance;
        history.Clear();

        byte[] pixels1 = [255, 0, 0, 255];
        var bmp1 = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels1, 4);
        var res1 = new DetectionResult(
            ClipDataType.Image,
            bmp1,
            "Image 1",
            "1x1",
            "",
            [],
            ImagePreview: bmp1
        );

        byte[] pixels2 = [255, 0, 0, 255];
        var bmp2 = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels2, 4);
        var res2 = new DetectionResult(
            ClipDataType.Image,
            bmp2,
            "Image 2",
            "1x1",
            "",
            [],
            ImagePreview: bmp2
        );

        history.Add(res1);
        Assert.Single(history.GetItems());

        history.Add(res2);
        Assert.Single(history.GetItems());

        history.Clear();
    }
}
