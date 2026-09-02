using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;
using MediaColor = System.Windows.Media.Color;

namespace ClipFlyout.Services;

public partial class DataTypeDetector : IDataTypeDetector
{
    private readonly ActionExecutor _executor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"\b(class|function|def|import|export|const|let|var|public|private|protected|namespace|using|interface|struct|enum|async|await|return|SELECT|FROM|WHERE|INSERT|UPDATE|DELETE)\b")]
    private static partial Regex CodeKeywordsRegex();

    public DataTypeDetector(ActionExecutor executor)
    {
        _executor = executor;
    }

    public DetectionResult? Detect(object clipboardData)
    {
        var cfg = _settings.Current;

        // 1. Check Image
        if (clipboardData is BitmapSource bitmap)
        {
            if (!cfg.DetectImage) return null;
            return DetectImage(bitmap);
        }

        // 2. Textual analysis
        if (clipboardData is string text)
        {
            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;

            // 2a. HEX Color
            if (cfg.DetectHexColor && HexColorRegex().IsMatch(trimmed))
            {
                var colorResult = TryDetectHexColor(trimmed);
                if (colorResult != null) return colorResult;
            }

            // 2b. JSON
            if (cfg.DetectJson && ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
            {
                var jsonResult = TryDetectJson(trimmed, text);
                if (jsonResult != null) return jsonResult;
            }

            // 2c. URL
            if (cfg.DetectUrl && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return DetectUrl(uri, trimmed);
            }

            // 2d. Code Snippet
            if (cfg.DetectCode && IsCodeSnippet(text))
            {
                return DetectCodeSnippet(text);
            }

            // 2e. Fallback: Plain Text
            if (cfg.DetectPlainText)
            {
                return DetectPlainText(text);
            }

            return null;
        }

        return null;
    }

    private DetectionResult DetectImage(BitmapSource bitmap)
    {
        int width = (int)bitmap.PixelWidth;
        int height = (int)bitmap.PixelHeight;
        string gcdRatio = GetAspectRatio(width, height);

        var actions = new List<ActionItem>
        {
            new(
                "Action_SavePng",
                _loc.Get("Action_SavePng"),
                "Save24",
                _loc.Get("Action_SavePng_Desc"),
                () => _executor.SaveImageToFile(bitmap)
            ),
            new(
                "Action_CopyImageInfo",
                _loc.Get("Action_CopyImageInfo"),
                "Info24",
                _loc.Get("Action_CopyImageInfo_Desc"),
                () => _executor.CopyText($"{width}x{height} ({gcdRatio})", "Toast_Copied")
            )
        };

        return new DetectionResult(
            Type: ClipDataType.Image,
            RawData: bitmap,
            PreviewTitle: _loc.Get("Type_Image"),
            PreviewSubtitle: $"{width} × {height} px ({gcdRatio})",
            PreviewBody: string.Empty,
            AvailableActions: actions,
            ImagePreview: bitmap,
            BadgeText: $"{width}×{height}"
        );
    }

    private DetectionResult? TryDetectHexColor(string hex)
    {
        try
        {
            string clean = hex.TrimStart('#');
            byte a = 255, r = 0, g = 0, b = 0;

            if (clean.Length == 3) // #RGB
            {
                r = Convert.ToByte(new string(clean[0], 2), 16);
                g = Convert.ToByte(new string(clean[1], 2), 16);
                b = Convert.ToByte(new string(clean[2], 2), 16);
            }
            else if (clean.Length == 4) // #RGBA
            {
                r = Convert.ToByte(new string(clean[0], 2), 16);
                g = Convert.ToByte(new string(clean[1], 2), 16);
                b = Convert.ToByte(new string(clean[2], 2), 16);
                a = Convert.ToByte(new string(clean[3], 2), 16);
            }
            else if (clean.Length == 6) // #RRGGBB
            {
                r = Convert.ToByte(clean[..2], 16);
                g = Convert.ToByte(clean.Substring(2, 2), 16);
                b = Convert.ToByte(clean.Substring(4, 2), 16);
            }
            else if (clean.Length == 8) // #RRGGBBAA
            {
                r = Convert.ToByte(clean[..2], 16);
                g = Convert.ToByte(clean.Substring(2, 2), 16);
                b = Convert.ToByte(clean.Substring(4, 2), 16);
                a = Convert.ToByte(clean.Substring(6, 2), 16);
            }

            var mediaColor = MediaColor.FromArgb(a, r, g, b);
            string rgbStr = $"rgb({r}, {g}, {b})";
            string rgbaStr = $"rgba({r}, {g}, {b}, {Math.Round(a / 255.0, 2).ToString(CultureInfo.InvariantCulture)})";
            var (h, s, l) = RgbToHsl(r, g, b);
            string hslStr = $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";

            var actions = new List<ActionItem>
            {
                new(
                    "Action_CopyRgb",
                    _loc.Get("Action_CopyRgb"),
                    "Color24",
                    _loc.Get("Action_CopyRgb_Desc"),
                    () => _executor.CopyText(rgbStr, "Toast_Copied")
                ),
                new(
                    "Action_CopyHsl",
                    _loc.Get("Action_CopyHsl"),
                    "Dial24",
                    _loc.Get("Action_CopyHsl_Desc"),
                    () => _executor.CopyText(hslStr, "Toast_Copied")
                ),
                new(
                    "Action_CopyRgba",
                    _loc.Get("Action_CopyRgba"),
                    "Copy24",
                    _loc.Get("Action_CopyRgba_Desc"),
                    () => _executor.CopyText(rgbaStr, "Toast_Copied")
                )
            };

            return new DetectionResult(
                Type: ClipDataType.HexColor,
                RawData: hex,
                PreviewTitle: hex.ToUpperInvariant(),
                PreviewSubtitle: $"{rgbStr}  •  {hslStr}",
                PreviewBody: $"RGB: {r}, {g}, {b} | Alpha: {a}",
                AvailableActions: actions,
                HexColorCode: hex.ToUpperInvariant(),
                ColorValue: mediaColor,
                BadgeText: "Color"
            );
        }
        catch
        {
            return null;
        }
    }

    private DetectionResult? TryDetectJson(string trimmedJson, string originalText)
    {
        try
        {
            using var doc = JsonDocument.Parse(trimmedJson);
            var root = doc.RootElement;
            string subtitle;
            if (root.ValueKind == JsonValueKind.Object)
            {
                int count = root.EnumerateObject().Count();
                subtitle = $"JSON Object ({count} {(count == 1 ? "property" : "properties")})";
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                int count = root.EnumerateArray().Count();
                subtitle = $"JSON Array ({count} {(count == 1 ? "item" : "items")})";
            }
            else
            {
                subtitle = "JSON Value";
            }

            var actions = new List<ActionItem>
            {
                new(
                    "Action_FormatJson",
                    _loc.Get("Action_FormatJson"),
                    "Code24",
                    _loc.Get("Action_FormatJson_Desc"),
                    () =>
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        string formatted = JsonSerializer.Serialize(root, options);
                        _executor.CopyText(formatted, "Toast_FormattedJsonCopied");
                    }
                ),
                new(
                    "Action_MinifyJson",
                    _loc.Get("Action_MinifyJson"),
                    "Compress24",
                    _loc.Get("Action_MinifyJson_Desc"),
                    () =>
                    {
                        string minified = JsonSerializer.Serialize(root);
                        _executor.CopyText(minified, "Toast_MinifiedJsonCopied");
                    }
                )
            };

            string previewSnippet = trimmedJson.Length > 180 ? trimmedJson[..180] + "..." : trimmedJson;

            return new DetectionResult(
                Type: ClipDataType.Json,
                RawData: originalText,
                PreviewTitle: _loc.Get("Type_Json"),
                PreviewSubtitle: subtitle,
                PreviewBody: previewSnippet,
                AvailableActions: actions,
                BadgeText: "JSON"
            );
        }
        catch
        {
            return null;
        }
    }

    private DetectionResult DetectUrl(Uri uri, string fullUrl)
    {
        var actions = new List<ActionItem>
        {
            new(
                "Action_OpenBrowser",
                _loc.Get("Action_OpenBrowser"),
                "Globe24",
                _loc.Get("Action_OpenBrowser_Desc"),
                () => _executor.OpenBrowser(fullUrl)
            ),
            new(
                "Action_CopyQrCode",
                _loc.Get("Action_CopyQrCode"),
                "QrCode24",
                _loc.Get("Action_CopyQrCode_Desc"),
                () => _executor.GenerateAndCopyQrCode(fullUrl)
            ),
            new(
                "Action_CopyDomain",
                _loc.Get("Action_CopyDomain"),
                "Link24",
                _loc.Get("Action_CopyDomain_Desc"),
                () => _executor.CopyText(uri.Host, "Toast_Copied")
            )
        };

        return new DetectionResult(
            Type: ClipDataType.Url,
            RawData: fullUrl,
            PreviewTitle: uri.Host,
            PreviewSubtitle: uri.AbsolutePath.Length > 1 ? uri.AbsolutePath : uri.Scheme + "://",
            PreviewBody: fullUrl,
            AvailableActions: actions,
            BadgeText: "URL"
        );
    }

    private bool IsCodeSnippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10) return false;

        int lines = text.Split('\n').Length;
        int keywordMatches = CodeKeywordsRegex().Matches(text).Count;
        bool hasBraces = text.Contains('{') && text.Contains('}');
        bool hasSemicolons = text.Count(c => c == ';') >= 2;
        bool hasIndentation = text.Contains("    ") || text.Contains("\t");

        return (keywordMatches >= 2) || (keywordMatches >= 1 && (hasBraces || hasSemicolons || hasIndentation)) || (lines >= 3 && hasBraces);
    }

    private DetectionResult DetectCodeSnippet(string codeText)
    {
        int lineCount = codeText.Split('\n').Length;
        int charCount = codeText.Length;

        var actions = new List<ActionItem>
        {
            new(
                "Action_AdjustIndent",
                _loc.Get("Action_AdjustIndent"),
                "TextBulletListSquare24",
                _loc.Get("Action_AdjustIndent_Desc"),
                () =>
                {
                    string normalized = NormalizeIndentation(codeText);
                    _executor.CopyText(normalized, "Toast_Copied");
                }
            ),
            new(
                "Action_EscapeHtml",
                _loc.Get("Action_EscapeHtml"),
                "CodeSquare24",
                _loc.Get("Action_EscapeHtml_Desc"),
                () =>
                {
                    string escaped = WebUtility.HtmlEncode(codeText);
                    _executor.CopyText(escaped, "Toast_Copied");
                }
            )
        };

        string snippet = codeText.Length > 180 ? codeText[..180] + "..." : codeText;

        return new DetectionResult(
            Type: ClipDataType.Code,
            RawData: codeText,
            PreviewTitle: _loc.Get("Type_Code"),
            PreviewSubtitle: $"{lineCount} {(lineCount == 1 ? "line" : "lines")} • {charCount} chars",
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: "Code"
        );
    }

    private DetectionResult DetectPlainText(string text)
    {
        string trimmed = text.Trim();
        int charCount = text.Length;
        int wordCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        int lineCount = text.Split('\n').Length;

        var actions = new List<ActionItem>
        {
            new(
                "Action_TrimWhitespace",
                _loc.Get("Action_TrimWhitespace"),
                "Cut24",
                _loc.Get("Action_TrimWhitespace_Desc"),
                () => _executor.CopyText(trimmed, "Toast_Copied")
            ),
            new(
                "Action_TextStats",
                _loc.Get("Action_TextStats"),
                "DocumentText24",
                _loc.Get("Action_TextStats_Desc"),
                () =>
                {
                    string stats = $"Chars: {charCount}, Words: {wordCount}, Lines: {lineCount}";
                    _executor.CopyText(stats, "Toast_Copied");
                }
            ),
            new(
                "Action_UpperCase",
                _loc.Get("Action_UpperCase"),
                "TextCaseUppercase24",
                _loc.Get("Action_UpperCase_Desc"),
                () => _executor.CopyText(text.ToUpper(), "Toast_Copied")
            ),
            new(
                "Action_LowerCase",
                _loc.Get("Action_LowerCase"),
                "TextCaseLowercase24",
                _loc.Get("Action_LowerCase_Desc"),
                () => _executor.CopyText(text.ToLower(), "Toast_Copied")
            )
        };

        string snippet = trimmed.Length > 180 ? trimmed[..180] + "..." : trimmed;

        return new DetectionResult(
            Type: ClipDataType.PlainText,
            RawData: text,
            PreviewTitle: _loc.Get("Type_PlainText"),
            PreviewSubtitle: $"{charCount} chars • {wordCount} words",
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: "Text"
        );
    }

    private static string NormalizeIndentation(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var resultLines = new List<string>();
        foreach (var line in lines)
        {
            int leadingTabs = 0;
            while (leadingTabs < line.Length && line[leadingTabs] == '\t')
            {
                leadingTabs++;
            }
            if (leadingTabs > 0)
            {
                resultLines.Add(new string(' ', leadingTabs * 2) + line[leadingTabs..]);
            }
            else
            {
                resultLines.Add(line);
            }
        }
        return string.Join(Environment.NewLine, resultLines);
    }

    private static (double h, double s, double l) RgbToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (delta != 0)
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
                h = ((gd - bd) / delta) + (gd < bd ? 6 : 0);
            else if (max == gd)
                h = ((bd - rd) / delta) + 2;
            else
                h = ((rd - gd) / delta) + 4;

            h *= 60;
        }

        return (h, s, l);
    }

    private static string GetAspectRatio(int w, int h)
    {
        if (h == 0) return "1:1";
        int gcd = GCD(w, h);
        return $"{w / gcd}:{h / gcd}";
    }

    private static int GCD(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return Math.Max(1, a);
    }
}
