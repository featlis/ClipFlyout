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
}
