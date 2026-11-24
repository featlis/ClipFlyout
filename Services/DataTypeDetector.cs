using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;
using MediaColor = System.Windows.Media.Color;

namespace ClipFlyout.Services;

public partial class DataTypeDetector : IDataTypeDetector
{
    private const int MaxStructuredTextLength = 1_000_000;
    private const int MaxJsonLength = 512_000;
    private const int MaxBase64Length = 1_000_000;
    private const int MaxTableRows = 5_000;
    private const int MaxTableColumns = 100;

    // Preserve non-ASCII / CJK characters unescaped instead of turning Japanese into \uXXXX
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions MinifiedJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ActionExecutor _executor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly SettingsService _settings;

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^rgba?\(\s*(?<r>\d{1,3})(?:\s*,\s*|\s+)(?<g>\d{1,3})(?:\s*,\s*|\s+)(?<b>\d{1,3})(?:\s*(?:,|/)\s*(?<a>(?:\d{1,3}(?:\.\d+)?%|0|1(?:\.0+)?|0?\.\d+)))?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColorRegex();

    [GeneratedRegex(@"^hsla?\(\s*(?<h>\d{1,3}(?:\.\d+)?)(?:deg)?(?:\s*,\s*|\s+)(?<s>\d{1,3}(?:\.\d+)?)%(?:\s*,\s*|\s+)(?<l>\d{1,3}(?:\.\d+)?)%(?:\s*(?:,|/)\s*(?<a>(?:\d{1,3}(?:\.\d+)?%|0|1(?:\.0+)?|0?\.\d+)))?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex HslColorRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,9})?)?(?:Z|[+-]\d{2}:?\d{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex IsoDateTimeRegex();

    [GeneratedRegex(@"\b(class|function|def|import|export|const|let|var|public|private|protected|namespace|using|interface|struct|enum|async|await|return|SELECT|FROM|WHERE|INSERT|UPDATE|DELETE)\b")]
    private static partial Regex CodeKeywordsRegex();

    [GeneratedRegex(@"^\d{10}$|^\d{13}$")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^(?<local>[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+)@(?<domain>(?:[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?\.)+[A-Z]{2,63})$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    public DataTypeDetector(ActionExecutor executor, SettingsService? settings = null)
    {
        _executor = executor;
        _settings = settings ?? SettingsService.Instance;
    }

    public DetectionResult? Detect(object clipboardData)
    {
        var cfg = _settings.Current;

        // 0. FileDropList (Explorer copied files/folders)
        if (clipboardData is string[] filePaths && filePaths.Length > 0)
        {
            if (!cfg.DetectFileDrop) return null;
            return DetectFileDrop(filePaths);
        }

        // 1. Image
        if (clipboardData is BitmapSource bitmap)
        {
            if (!cfg.DetectImage) return null;
            return DetectImage(bitmap);
        }

        // 2. Textual analysis
        if (clipboardData is string text)
        {
            // Fast check for huge text (> 1 MB) without trimming/allocating the entire buffer
            if (text.Length > MaxStructuredTextLength)
            {
                return cfg.DetectPlainText ? DetectLargePlainText(text) : null;
            }

            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;

            // 2a. Color (HEX, rgb/rgba, hsl/hsla)
            if (cfg.DetectHexColor)
            {
                if (HexColorRegex().IsMatch(trimmed))
                {
                    var colorResult = TryDetectHexColor(trimmed);
                    if (colorResult != null) return colorResult;
                }
                else if (trimmed.Length <= 48 &&
                         (trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) ||
                          trimmed.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)))
                {
                    var cssColor = TryDetectCssColor(trimmed);
                    if (cssColor != null) return cssColor;
                }
            }

            // 2b. Unix Timestamp
            if (cfg.DetectTimestamp && TimestampRegex().IsMatch(trimmed))
            {
                var tsResult = TryDetectTimestamp(trimmed);
                if (tsResult != null) return tsResult;
            }

            // 2b-2. ISO 8601 DateTime
            if (cfg.DetectIsoDateTime && trimmed.Length <= 40 && IsoDateTimeRegex().IsMatch(trimmed))
            {
                var isoResult = TryDetectIsoDateTime(trimmed);
                if (isoResult != null) return isoResult;
            }

            // 2c. JSON
            if (cfg.DetectJson && trimmed.Length <= MaxJsonLength &&
                ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
            {
                var jsonResult = TryDetectJson(trimmed, text);
                if (jsonResult != null) return jsonResult;
            }

            // 2d. URL (reject whitespace inside string and empty hosts)
            if (cfg.DetectUrl &&
                !ContainsWhitespace(trimmed) &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                return DetectUrl(uri, trimmed);
            }

            // 2e. Email address
            if (cfg.DetectEmail && trimmed.Length <= 254 && EmailRegex().Match(trimmed) is { Success: true } emailMatch)
            {
                return DetectEmail(trimmed, emailMatch.Groups["local"].Value, emailMatch.Groups["domain"].Value);
            }

            // 2e-2. JWT Token
            if (cfg.EnableJwtDetector && trimmed.Length <= MaxBase64Length &&
                trimmed.StartsWith("ey", StringComparison.Ordinal) &&
                !ContainsWhitespace(trimmed) &&
                trimmed.Count(c => c == '.') == 2)
            {
                var jwtResult = TryDetectJwt(trimmed);
                if (jwtResult != null) return jwtResult;
            }

            // 2f. Base64
            if (cfg.DetectBase64 && trimmed.Length <= MaxBase64Length &&
                (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || LooksLikeBase64(trimmed)))
            {
                var b64Result = TryDetectBase64(trimmed);
                if (b64Result != null) return b64Result;
            }

            // 2g. Table Data (CSV / TSV)
            if (cfg.DetectTable)
            {
                var tableResult = TryDetectTableData(trimmed);
                if (tableResult != null) return tableResult;
            }

            // 2h. Code Snippet
            if (cfg.DetectCode && IsCodeSnippet(text))
            {
                return DetectCodeSnippet(text);
            }

            // 2i. Fallback: Plain Text
            if (cfg.DetectPlainText)
            {
                return DetectPlainText(text);
            }

            return null;
        }

        return null;
    }

    private static bool ContainsWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i])) return true;
        }
        return false;
    }

    private DetectionResult DetectFileDrop(string[] paths)
    {
        string joinedPaths = string.Join(Environment.NewLine, paths);
        string quotedPaths = string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\""));
        string fileNames = string.Join(Environment.NewLine, paths.Select(p =>
        {
            string name = Path.GetFileName(p.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? p : name;
        }));

        string firstPath = paths[0];
        string firstName = Path.GetFileName(firstPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(firstName)) firstName = firstPath;

        long totalBytes = 0;
        int fileCount = 0;
        int dirCount = 0;
        foreach (var p in paths.Take(50))
        {
            try
            {
                if (Directory.Exists(p)) dirCount++;
                else if (File.Exists(p))
                {
                    fileCount++;
                    totalBytes += new FileInfo(p).Length;
                }
            }
            catch { }
        }

        string sizePart = totalBytes > 0 ? $" • {FormatByteSize(totalBytes)}" : string.Empty;
        string subtitle = paths.Length == 1
            ? (Directory.Exists(firstPath) ? firstPath : $"{Path.GetDirectoryName(firstPath)}{sizePart}")
            : _loc.Get("Sub_FileDrop_Items", paths.Length) + sizePart;

        string previewBody = string.Join(Environment.NewLine, paths.Take(4).Select(p =>
        {
            string name = Path.GetFileName(p.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? p : name;
        }));
        if (paths.Length > 4) previewBody += $"{Environment.NewLine}{_loc.Get("Sub_FileDrop_More", paths.Length - 4)}";

        var actions = new List<ActionItem>
        {
            new(
                "Action_CopyFilePaths",
                _loc.Get("Action_CopyFilePaths"),
                "File24",
                _loc.Get("Action_CopyFilePaths_Desc"),
                () => _executor.CopyText(joinedPaths, "Toast_Copied"),
                IsPrimary: true,
                Subtitle: paths.Length == 1 ? firstPath : _loc.Get("Sub_FileDrop_Paths", paths.Length)
            ),
            new(
                "Action_CopyQuotedPaths",
                _loc.Get("Action_CopyQuotedPaths"),
                "Code24",
                _loc.Get("Action_CopyQuotedPaths_Desc"),
                () => _executor.CopyText(quotedPaths, "Toast_Copied"),
                Subtitle: $"\"{firstName}\""
            ),
            new(
                "Action_CopyFileNames",
                _loc.Get("Action_CopyFileNames"),
                "Copy24",
                _loc.Get("Action_CopyFileNames_Desc"),
                () => _executor.CopyText(fileNames, "Toast_Copied"),
                Subtitle: firstName
            ),
            new(
                "Action_OpenFolder",
                _loc.Get("Action_OpenFolder"),
                "Folder24",
                _loc.Get("Action_OpenFolder_Desc"),
                () => _executor.OpenContainingFolder(firstPath)
            )
        };

        return new DetectionResult(
            Type: ClipDataType.FileDrop,
            RawData: paths,
            PreviewTitle: paths.Length == 1 ? firstName : $"{firstName} (+{paths.Length - 1})",
            PreviewSubtitle: subtitle,
            PreviewBody: previewBody,
            AvailableActions: actions,
            BadgeText: paths.Length == 1 ? _loc.Get("Badge_FileSingle") : _loc.Get("Badge_FileMulti", paths.Length)
        );
    }

    private DetectionResult DetectImage(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        string gcdRatio = GetAspectRatio(width, height);

        var actions = new List<ActionItem>
        {
            new(
                "Action_SavePng",
                _loc.Get("Action_SavePng"),
                "Save24",
                _loc.Get("Action_SavePng_Desc"),
                () => _executor.SaveImageToFile(bitmap),
                IsPrimary: true,
                Subtitle: "PNG / JPG / BMP"
            ),
            new(
                "Action_CopyImageInfo",
                _loc.Get("Action_CopyImageInfo"),
                "Info24",
                _loc.Get("Action_CopyImageInfo_Desc"),
                () => _executor.CopyText($"{width}x{height} ({gcdRatio})", "Toast_Copied"),
                Subtitle: $"{width} × {height} ({gcdRatio})"
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

            if (clean.Length == 3)
            {
                r = Convert.ToByte(new string(clean[0], 2), 16);
                g = Convert.ToByte(new string(clean[1], 2), 16);
                b = Convert.ToByte(new string(clean[2], 2), 16);
            }
            else if (clean.Length == 4)
            {
                r = Convert.ToByte(new string(clean[0], 2), 16);
                g = Convert.ToByte(new string(clean[1], 2), 16);
                b = Convert.ToByte(new string(clean[2], 2), 16);
                a = Convert.ToByte(new string(clean[3], 2), 16);
            }
            else if (clean.Length == 6)
            {
                r = Convert.ToByte(clean[..2], 16);
                g = Convert.ToByte(clean.Substring(2, 2), 16);
                b = Convert.ToByte(clean.Substring(4, 2), 16);
            }
            else if (clean.Length == 8)
            {
                r = Convert.ToByte(clean[..2], 16);
                g = Convert.ToByte(clean.Substring(2, 2), 16);
                b = Convert.ToByte(clean.Substring(4, 2), 16);
                a = Convert.ToByte(clean.Substring(6, 2), 16);
            }

            return BuildColorResult(hex, hex.ToUpperInvariant(), a, r, g, b);
        }
        catch
        {
            return null;
        }
    }

    private DetectionResult? TryDetectCssColor(string raw)
    {
        try
        {
            var rgbMatch = RgbColorRegex().Match(raw);
            if (rgbMatch.Success)
            {
                int ri = int.Parse(rgbMatch.Groups["r"].Value, CultureInfo.InvariantCulture);
                int gi = int.Parse(rgbMatch.Groups["g"].Value, CultureInfo.InvariantCulture);
                int bi = int.Parse(rgbMatch.Groups["b"].Value, CultureInfo.InvariantCulture);
                if (ri > 255 || gi > 255 || bi > 255) return null;

                byte a = 255;
                if (rgbMatch.Groups["a"].Success)
                {
                    a = ParseCssAlpha(rgbMatch.Groups["a"].Value);
                }

                string hex = a == 255 ? $"#{ri:X2}{gi:X2}{bi:X2}" : $"#{ri:X2}{gi:X2}{bi:X2}{a:X2}";
                return BuildColorResult(raw, hex, a, (byte)ri, (byte)gi, (byte)bi);
            }

            var hslMatch = HslColorRegex().Match(raw);
            if (hslMatch.Success)
            {
                double h = double.Parse(hslMatch.Groups["h"].Value, CultureInfo.InvariantCulture);
                double s = double.Parse(hslMatch.Groups["s"].Value, CultureInfo.InvariantCulture);
                double l = double.Parse(hslMatch.Groups["l"].Value, CultureInfo.InvariantCulture);
                if (h > 360 || s > 100 || l > 100) return null;

                byte a = 255;
                if (hslMatch.Groups["a"].Success)
                {
                    a = ParseCssAlpha(hslMatch.Groups["a"].Value);
                }

                var (r, g, b) = HslToRgb(h, s / 100.0, l / 100.0);
                string hex = a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
                return BuildColorResult(raw, hex, a, r, g, b);
            }
        }
        catch
        {
        }
        return null;
    }

    private DetectionResult BuildColorResult(string rawInput, string hexUpper, byte a, byte r, byte g, byte b)
    {
        var mediaColor = MediaColor.FromArgb(a, r, g, b);
        string rgbStr = $"rgb({r}, {g}, {b})";
        string rgbaStr = $"rgba({r}, {g}, {b}, {Math.Round(a / 255.0, 2).ToString(CultureInfo.InvariantCulture)})";
        var (h, s, l) = RgbToHsl(r, g, b);
        string hslStr = $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";

        var actions = new List<ActionItem>
        {
            new(
                "Action_CopyHex",
                _loc.Get("Action_CopyHex"),
                "Copy24",
                _loc.Get("Action_CopyHex_Desc"),
                () => _executor.CopyText(hexUpper, "Toast_Copied"),
                IsPrimary: true,
                Subtitle: hexUpper,
                IconGlyph: "\uE8C8"
            ),
            new(
                "Action_CopyRgb",
                _loc.Get("Action_CopyRgb"),
                "Color24",
                _loc.Get("Action_CopyRgb_Desc"),
                () => _executor.CopyText(rgbStr, "Toast_Copied"),
                Subtitle: rgbStr,
                IconGlyph: "\uE790"
            ),
            new(
                "Action_CopyHsl",
                _loc.Get("Action_CopyHsl"),
                "Convert24",
                _loc.Get("Action_CopyHsl_Desc"),
                () => _executor.CopyText(hslStr, "Toast_Copied"),
                Subtitle: hslStr,
                IconGlyph: "\uE8AB"
            ),
            new(
                "Action_CopyRgba",
                _loc.Get("Action_CopyRgba"),
                "Copy24",
                _loc.Get("Action_CopyRgba_Desc"),
                () => _executor.CopyText(rgbaStr, "Toast_Copied"),
                Subtitle: rgbaStr,
                IconGlyph: "\uE8C8"
            )
        };

        return new DetectionResult(
            Type: ClipDataType.HexColor,
            RawData: rawInput,
            PreviewTitle: hexUpper,
            PreviewSubtitle: _loc.Get("Type_HexColor_Desc"),
            PreviewBody: $"{rgbStr}  •  {hslStr}",
            AvailableActions: actions,
            HexColorCode: hexUpper,
            ColorValue: mediaColor,
            BadgeText: "Color"
        );
    }

    private DetectionResult? TryDetectTimestamp(string text)
    {
        try
        {
            // Reject common ISBN-13 / EAN-13 / JAN barcodes (978/979/490-499 prefixes)
            if (text.Length == 13 && (text.StartsWith("978") || text.StartsWith("979")))
            {
                return null;
            }

            if (!long.TryParse(text, out long val)) return null;

            DateTimeOffset dto = text.Length == 10
                ? DateTimeOffset.FromUnixTimeSeconds(val)
                : DateTimeOffset.FromUnixTimeMilliseconds(val);

            // Narrow to realistic modern epoch range (1995-2100) to avoid matching arbitrary IDs
            if (dto.Year < 1995 || dto.Year > 2100) return null;

            var local = dto.ToLocalTime();
            string localStr = local.ToString("yyyy-MM-dd HH:mm:ss");
            string isoStr = dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string subtitle = $"UTC: {dto.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z ({local:zzz})";

            var actions = new List<ActionItem>
            {
                new(
                    "Action_CopyLocalDate",
                    _loc.Get("Action_CopyLocalDate"),
                    "Calendar24",
                    _loc.Get("Action_CopyLocalDate_Desc"),
                    () => _executor.CopyText(localStr, "Toast_Copied"),
                    IsPrimary: true,
                    Subtitle: localStr
                ),
                new(
                    "Action_CopyIsoDate",
                    _loc.Get("Action_CopyIsoDate"),
                    "Globe24",
                    _loc.Get("Action_CopyIsoDate_Desc"),
                    () => _executor.CopyText(isoStr, "Toast_Copied"),
                    Subtitle: isoStr
                ),
                new(
                    "Action_CopyCurrentTimestamp",
                    _loc.Get("Action_CopyCurrentTimestamp"),
                    "Clock24",
                    _loc.Get("Action_CopyCurrentTimestamp_Desc"),
                    () => _executor.CopyText(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "Toast_Copied")
                )
            };

            return new DetectionResult(
                Type: ClipDataType.UnixTimestamp,
                RawData: text,
                PreviewTitle: localStr,
                PreviewSubtitle: subtitle,
                PreviewBody: $"Epoch: {text} ({(text.Length == 10 ? "sec" : "ms")})",
                AvailableActions: actions,
                BadgeText: "Timestamp"
            );
        }
        catch
        {
            return null;
        }
    }

    private DetectionResult? TryDetectIsoDateTime(string text)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            return null;
        }

        var local = dto.ToLocalTime();
        string localStr = local.ToString("yyyy-MM-dd HH:mm:ss");
        string unixSec = dto.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string unixMs = dto.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var actions = new List<ActionItem>
        {
            new(
                "Action_CopyLocalDate",
                _loc.Get("Action_CopyLocalDate"),
                "Calendar24",
                _loc.Get("Action_CopyLocalDate_Desc"),
                () => _executor.CopyText(localStr, "Toast_Copied"),
                IsPrimary: true,
                Subtitle: localStr
            ),
            new(
                "Action_CopyUnixSeconds",
                _loc.Get("Action_CopyUnixSeconds"),
                "Clock24",
                _loc.Get("Action_CopyUnixSeconds_Desc"),
                () => _executor.CopyText(unixSec, "Toast_Copied"),
                Subtitle: unixSec
            ),
            new(
                "Action_CopyUnixMillis",
                _loc.Get("Action_CopyUnixMillis"),
                "Clock24",
                _loc.Get("Action_CopyUnixMillis_Desc"),
                () => _executor.CopyText(unixMs, "Toast_Copied"),
                Subtitle: unixMs
            )
        };

        return new DetectionResult(
            Type: ClipDataType.IsoDateTime,
            RawData: text,
            PreviewTitle: localStr,
            PreviewSubtitle: $"UTC: {dto.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z ({local:zzz})",
            PreviewBody: text,
            AvailableActions: actions,
            BadgeText: "ISO Date"
        );
    }

    private DetectionResult? TryDetectBase64(string text)
    {
        try
        {
            string payload = text;
            bool isDataUri = text.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
            string? mimeType = null;

            if (isDataUri)
            {
                int commaIdx = text.IndexOf(',');
                if (commaIdx == -1) return null;
                string header = text[..commaIdx];
                if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;
                payload = text[(commaIdx + 1)..];

                int semiIdx = header.IndexOf(';');
                if (semiIdx > 5)
                {
                    mimeType = header[5..semiIdx];
                }
            }

            if (payload.Length > MaxBase64Length) return null;

            // Strip MIME line breaks and normalize URL-safe Base64
            string normalized = payload.Replace("\r", "").Replace("\n", "").Trim();
            if (normalized.Contains('-') || normalized.Contains('_'))
            {
                normalized = normalized.Replace('-', '+').Replace('_', '/');
            }
            int mod = normalized.Length % 4;
            if (mod == 1) return null;
            if (mod == 2) normalized += "==";
            else if (mod == 3) normalized += "=";

            byte[] bytes = Convert.FromBase64String(normalized);
            if (bytes.Length < 4) return null;

            bool isImage = (mimeType != null && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) || IsImageBytes(bytes);

            var actions = new List<ActionItem>();

            if (isImage)
            {
                actions.Add(new(
                    "Action_CopyDecodedImage",
                    _loc.Get("Action_CopyDecodedImage"),
                    "Image24",
                    _loc.Get("Action_CopyDecodedImage_Desc"),
                    () => _executor.CopyBase64Image(bytes),
                    IsPrimary: true,
                    Subtitle: FormatByteSize(bytes.Length)
                ));
            }

            string? decodedText = TryDecodeUtf8Text(bytes);
            bool isPrintableText = decodedText is { Length: > 0 } &&
                decodedText.All(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t');

            // Allow non-ASCII UTF-8 strings (e.g. unpadded Japanese Base64) while still filtering control-char garbage
            if (!isDataUri && !isImage)
            {
                bool hasBase64Signals = payload.Contains('=') || payload.Contains('+') || payload.Contains('/') || payload.Contains('-') || payload.Contains('_');
                if (!hasBase64Signals && !isPrintableText)
                {
                    return null;
                }
                if (!hasBase64Signals && decodedText != null && decodedText.All(c => char.IsControl(c) || c == '\uFFFD'))
                {
                    return null;
                }
            }

            if (isPrintableText && decodedText != null)
            {
                string previewSub = decodedText.Length > 36 ? decodedText[..36].Replace('\n', ' ') + "…" : decodedText.Replace('\n', ' ');
                actions.Add(new(
                    "Action_DecodeBase64",
                    _loc.Get("Action_DecodeBase64"),
                    "Decode24",
                    _loc.Get("Action_DecodeBase64_Desc"),
                    () => _executor.CopyText(decodedText, "Toast_Base64Decoded"),
                    IsPrimary: actions.Count == 0,
                    Subtitle: previewSub
                ));
            }

            if (actions.Count == 0) return null;

            string title = isImage ? _loc.Get("Type_Base64Image") : _loc.Get("Type_Base64Text");
            string subtitle = $"{FormatByteSize(bytes.Length)}";
            string snippet = isPrintableText && decodedText != null
                ? (decodedText.Length > 140 ? decodedText[..140] + "…" : decodedText)
                : $"{mimeType ?? "binary"} ({FormatByteSize(bytes.Length)})";

            return new DetectionResult(
                Type: ClipDataType.Base64,
                RawData: text,
                PreviewTitle: title,
                PreviewSubtitle: subtitle,
                PreviewBody: snippet,
                AvailableActions: actions,
                BadgeText: "Base64"
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeBase64(string s)
    {
        int effectiveLen = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\r' || c == '\n') continue;
            bool ok = (c >= 'A' && c <= 'Z') ||
                      (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') ||
                      c == '+' || c == '/' || c == '=' || c == '-' || c == '_';
            if (!ok) return false;
            effectiveLen++;
        }
        return effectiveLen >= 16 && (effectiveLen % 4 != 1);
    }

    private static string? TryDecodeUtf8Text(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsImageBytes(byte[] bytes)
    {
        if (bytes.Length < 8) return false;
        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        // WebP: RIFF....WEBP (requires >= 12 bytes!)
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;

        return false;
    }

    private DetectionResult? TryDetectTableData(string text)
    {
        try
        {
            if (text.Length > MaxStructuredTextLength) return null;

            bool isTsv = LooksLikeTsv(text);
            List<string[]>? rows = isTsv
                ? ParseDelimitedRows(text, '\t')
                : ParseDelimitedRows(text, ',');

            if (rows == null || rows.Count < 2) return null;
            int colCount = rows[0].Length;
            if (colCount < 2 || colCount > MaxTableColumns || rows.Count > MaxTableRows) return null;
            if (rows.Any(r => r.Length != colCount)) return null;

            // Guard against 2-line English prose with a stray comma per line ("Dear Alice,\nThanks, Bob")
            if (!isTsv && colCount == 2 && rows.Count == 2)
            {
                bool looksLikeProse = rows.Any(r => r.Any(cell => cell.Length == 0)) ||
                                      rows[0][0].Contains(' ') && rows[0][1].Length == 0;
                if (looksLikeProse) return null;
            }

            // Compute Markdown & JSON lazily only when user clicks the action!
            var actions = new List<ActionItem>
            {
                new(
                    "Action_ToMarkdownTable",
                    _loc.Get("Action_ToMarkdownTable"),
                    "Table24",
                    _loc.Get("Action_ToMarkdownTable_Desc"),
                    () => _executor.CopyText(BuildMarkdownTable(rows, colCount), "Toast_MarkdownTableCopied"),
                    IsPrimary: true,
                    Subtitle: $"{rows.Count} × {colCount}"
                ),
                new(
                    "Action_ToJsonArray",
                    _loc.Get("Action_ToJsonArray"),
                    "Code24",
                    _loc.Get("Action_ToJsonArray_Desc"),
                    () => _executor.CopyText(BuildJsonTableArray(rows), "Toast_JsonArrayCopied"),
                    Subtitle: _loc.Get("Sub_Table_Objects", rows.Count - 1)
                )
            };

            string subtitle = _loc.Get("Sub_Table_Dims", rows.Count, colCount, isTsv ? "TSV" : "CSV");
            string snippet = string.Join("\n", rows.Take(3).Select(row => string.Join(isTsv ? "\t" : ", ", row)));
            if (snippet.Length > 180) snippet = snippet[..180] + "…";

            return new DetectionResult(
                Type: ClipDataType.TableData,
                RawData: text,
                PreviewTitle: _loc.Get("Type_TableData"),
                PreviewSubtitle: subtitle,
                PreviewBody: snippet,
                AvailableActions: actions,
                BadgeText: isTsv ? "TSV" : "CSV"
            );
        }
        catch
        {
            return null;
        }
    }

    private static string BuildMarkdownTable(List<string[]> rows, int colCount)
    {
        var mdSb = new StringBuilder();
        mdSb.AppendLine("| " + string.Join(" | ", rows[0].Select(EscapeMarkdownCell)) + " |");
        mdSb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", colCount)) + " |");
        for (int i = 1; i < rows.Count; i++)
        {
            mdSb.AppendLine("| " + string.Join(" | ", rows[i].Select(EscapeMarkdownCell)) + " |");
        }
        return mdSb.ToString().TrimEnd();
    }

    private static string BuildJsonTableArray(List<string[]> rows)
    {
        var jsonList = new List<Dictionary<string, string>>(Math.Max(0, rows.Count - 1));
        var headers = MakeUniqueHeaders(rows[0]);
        for (int i = 1; i < rows.Count; i++)
        {
            var dict = new Dictionary<string, string>(headers.Length);
            for (int j = 0; j < headers.Length; j++)
            {
                dict[headers[j]] = j < rows[i].Length ? rows[i][j] : string.Empty;
            }
            jsonList.Add(dict);
        }
        return JsonSerializer.Serialize(jsonList, IndentedJsonOptions);
    }

    private static bool LooksLikeTsv(string text)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(3)
            .ToList();

        return lines.Count >= 2 && lines.All(line => line.Contains('\t'));
    }

    private static List<string[]>? ParseDelimitedRows(string text, char separator)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool insideQuotes = false;
        bool atFieldStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '"' && (insideQuotes || atFieldStart))
            {
                if (insideQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                    atFieldStart = false;
                }
            }
            else if (!insideQuotes && current == separator)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
                atFieldStart = true;
            }
            else if (!insideQuotes && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString().Trim());
                cell.Clear();
                atFieldStart = true;

                if (row.Any(value => value.Length > 0))
                {
                    rows.Add(row.ToArray());
                    if (rows.Count > MaxTableRows) return null;
                }
                row.Clear();
            }
            else
            {
                if (!char.IsWhiteSpace(current)) atFieldStart = false;
                cell.Append(current);
            }
        }

        if (insideQuotes) return null;

        row.Add(cell.ToString().Trim());
        if (row.Any(value => value.Length > 0))
        {
            rows.Add(row.ToArray());
        }

        return rows;
    }

    private static string EscapeMarkdownCell(string value) =>
        value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    private static string[] MakeUniqueHeaders(string[] headers)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueHeaders = new string[headers.Length];

        for (int i = 0; i < headers.Length; i++)
        {
            string baseName = string.IsNullOrWhiteSpace(headers[i]) ? $"column_{i + 1}" : headers[i];
            string name = baseName;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName}_{suffix++}";
            }
            uniqueHeaders[i] = name;
        }

        return uniqueHeaders;
    }

    private DetectionResult? TryDetectJson(string trimmedJson, string originalText)
    {
        try
        {
            using var doc = JsonDocument.Parse(trimmedJson);
            var root = doc.RootElement.Clone();
            string subtitle;
            if (root.ValueKind == JsonValueKind.Object)
            {
                int count = root.EnumerateObject().Count();
                subtitle = _loc.Get("Sub_Json_Object", count);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                int count = root.EnumerateArray().Count();
                subtitle = _loc.Get("Sub_Json_Array", count);
            }
            else
            {
                subtitle = _loc.Get("Sub_Json_Value");
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
                        string formatted = JsonSerializer.Serialize(root, IndentedJsonOptions);
                        _executor.CopyTransformedText(formatted, _loc.Get("Toast_FormattedJsonCopied"));
                    },
                    IsPrimary: true,
                    Subtitle: _loc.Get("Sub_Json_Pretty")
                ),
                new(
                    "Action_MinifyJson",
                    _loc.Get("Action_MinifyJson"),
                    "Compress24",
                    _loc.Get("Action_MinifyJson_Desc"),
                    () =>
                    {
                        string minified = JsonSerializer.Serialize(root, MinifiedJsonOptions);
                        _executor.CopyTransformedText(minified, _loc.Get("Toast_MinifiedJsonCopied"));
                    },
                    Subtitle: _loc.Get("Sub_Json_Minify")
                )
            };

            string previewSnippet = trimmedJson.Length > 180 ? trimmedJson[..180] + "…" : trimmedJson;

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
                () => _executor.OpenBrowser(fullUrl),
                IsPrimary: true,
                Subtitle: uri.Host
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
                () => _executor.CopyText(uri.Host, "Toast_Copied"),
                Subtitle: uri.Host
            )
        };

        var cfg = _settings.Current;
        if (cfg.EnableCleanUrl && CleanUrlHelper.TryCleanUrl(fullUrl, out string cleanUrl))
        {
            actions.Insert(1, new(
                "Action_CopyCleanUrl",
                _loc.Get("Action_CopyCleanUrl"),
                "Clean24",
                _loc.Get("Action_CopyCleanUrl_Desc"),
                () => _executor.CopyTransformedText(cleanUrl, _loc.Get("Toast_CleanUrlCopied")),
                Subtitle: _loc.Get("Sub_CleanUrl")
            ));
        }

        if (fullUrl.Contains('%'))
        {
            string decoded = Uri.UnescapeDataString(fullUrl);
            if (!string.Equals(decoded, fullUrl, StringComparison.Ordinal))
            {
                actions.Add(new(
                    "Action_DecodeUrl",
                    _loc.Get("Action_DecodeUrl"),
                    "Decode24",
                    _loc.Get("Action_DecodeUrl_Desc"),
                    () => _executor.CopyTransformedText(decoded, _loc.Get("Toast_UrlDecoded"))
                ));
            }
        }

        return new DetectionResult(
            Type: ClipDataType.Url,
            RawData: fullUrl,
            PreviewTitle: uri.Host,
            PreviewSubtitle: uri.AbsolutePath.Length > 1 ? uri.AbsolutePath : uri.Scheme + "://",
            PreviewBody: fullUrl.Length > 160 ? fullUrl[..160] + "…" : fullUrl,
            AvailableActions: actions,
            BadgeText: "URL"
        );
    }

    private DetectionResult DetectEmail(string emailAddress, string localPart, string domain)
    {
        var actions = new List<ActionItem>
        {
            new("Action_OpenEmail", _loc.Get("Action_OpenEmail"), "Mail24", _loc.Get("Action_OpenEmail_Desc"), () => _executor.OpenEmail(emailAddress), IsPrimary: true, Subtitle: emailAddress),
            new("Action_CopyEmailDomain", _loc.Get("Action_CopyEmailDomain"), "Link24", _loc.Get("Action_CopyEmailDomain_Desc"), () => _executor.CopyText(domain, "Toast_Copied"), Subtitle: domain),
            new("Action_CopyEmailUser", _loc.Get("Action_CopyEmailUser"), "Person24", _loc.Get("Action_CopyEmailUser_Desc"), () => _executor.CopyText(localPart, "Toast_Copied"), Subtitle: localPart)
        };

        return new DetectionResult(
            Type: ClipDataType.Email,
            RawData: emailAddress,
            PreviewTitle: emailAddress,
            PreviewSubtitle: domain,
            PreviewBody: emailAddress,
            AvailableActions: actions,
            BadgeText: "Email");
    }

    private bool IsCodeSnippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10) return false;

        int lines = CountLines(text);
        int keywordMatches = CodeKeywordsRegex().Matches(text).Count;
        bool hasBraces = text.Contains('{') && text.Contains('}');
        bool hasSemicolons = text.Count(c => c == ';') >= 2;
        bool hasIndentation = text.Contains("    ") || text.Contains('\t');
        bool hasCodeSymbols = text.Contains("=>") || text.Contains("()") || text.Contains("::") || text.Contains("!=") || text.Contains("==");

        // Require actual code structure (braces/semicolons/indentation/operators/multiline)
        // so normal English prose containing "public" + "class" doesn't false-trigger.
        return (keywordMatches >= 1 && (hasBraces || hasSemicolons || hasIndentation || hasCodeSymbols)) ||
               (keywordMatches >= 3 && lines >= 2) ||
               (lines >= 3 && hasBraces);
    }

    private DetectionResult DetectCodeSnippet(string codeText)
    {
        int lineCount = CountLines(codeText);
        int charCount = codeText.Length;

        var actions = new List<ActionItem>
        {
            new(
                "Action_AdjustIndent",
                _loc.Get("Action_AdjustIndent"),
                "Code24",
                _loc.Get("Action_AdjustIndent_Desc"),
                () =>
                {
                    string normalized = NormalizeIndentation(codeText);
                    _executor.CopyTransformedText(normalized, _loc.Get("Toast_Copied"));
                },
                IsPrimary: true,
                Subtitle: _loc.Get("Sub_Code_Indent")
            ),
            new(
                "Action_EscapeHtml",
                _loc.Get("Action_EscapeHtml"),
                "CodeSquare24",
                _loc.Get("Action_EscapeHtml_Desc"),
                () =>
                {
                    string escaped = WebUtility.HtmlEncode(codeText);
                    _executor.CopyTransformedText(escaped, _loc.Get("Toast_Copied"));
                },
                Subtitle: "&lt; &gt; &amp;"
            )
        };

        string snippet = codeText.Length > 180 ? codeText[..180] + "…" : codeText;

        return new DetectionResult(
            Type: ClipDataType.Code,
            RawData: codeText,
            PreviewTitle: _loc.Get("Type_Code"),
            PreviewSubtitle: _loc.Get("Sub_Code_Stats", lineCount, charCount),
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: "Code"
        );
    }

    private DetectionResult DetectLargePlainText(string text)
    {
        int charCount = text.Length;
        int lineCount = CountLines(text);
        string snippet = HistoryService.NormalizeSingleLine(text);
        if (snippet.Length > 180) snippet = snippet[..180] + "…";

        var actions = new List<ActionItem>
        {
            new(
                "Action_TextStats",
                _loc.Get("Action_TextStats"),
                "Info24",
                _loc.Get("Action_TextStats_Desc"),
                () => _executor.CopyText($"Chars: {charCount}, Lines: {lineCount}", "Toast_Copied"),
                IsPrimary: true,
                Subtitle: _loc.Get("Sub_Text_LargeStats", charCount, lineCount)
            )
        };

        return new DetectionResult(
            Type: ClipDataType.PlainText,
            RawData: text,
            PreviewTitle: _loc.Get("Type_PlainText"),
            PreviewSubtitle: _loc.Get("Sub_Text_LargeStats", charCount, lineCount),
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: "Text"
        );
    }

    private DetectionResult DetectPlainText(string text)
    {
        string trimmed = text.Trim();
        int charCount = text.Length;
        int wordCount = CountWords(text);
        int lineCount = CountLines(text);

        var actions = new List<ActionItem>();

        bool hasExtraWhitespace = trimmed.Length != text.Length;
        if (hasExtraWhitespace)
        {
            actions.Add(new(
                "Action_TrimWhitespace",
                _loc.Get("Action_TrimWhitespace"),
                "Cut24",
                _loc.Get("Action_TrimWhitespace_Desc"),
                () => _executor.CopyTransformedText(trimmed, _loc.Get("Toast_Copied")),
                IsPrimary: true
            ));
        }

        actions.Add(new(
            "Action_TextStats",
            _loc.Get("Action_TextStats"),
            "Info24",
            _loc.Get("Action_TextStats_Desc"),
            () =>
            {
                string stats = $"Chars: {charCount}, Words: {wordCount}, Lines: {lineCount}";
                _executor.CopyText(stats, "Toast_Copied");
            },
            IsPrimary: actions.Count == 0,
            Subtitle: _loc.Get("Sub_Text_FullStats", charCount, wordCount, lineCount)
        ));

        bool hasLower = trimmed.Any(char.IsLower);
        bool hasUpper = trimmed.Any(char.IsUpper);

        if (hasLower)
        {
            actions.Add(new(
                "Action_UpperCase",
                _loc.Get("Action_UpperCase"),
                "TextCaseUppercase24",
                _loc.Get("Action_UpperCase_Desc"),
                () => _executor.CopyTransformedText(text.ToUpperInvariant(), _loc.Get("Toast_Copied"))
            ));
        }

        if (hasUpper)
        {
            actions.Add(new(
                "Action_LowerCase",
                _loc.Get("Action_LowerCase"),
                "TextCaseLowercase24",
                _loc.Get("Action_LowerCase_Desc"),
                () => _executor.CopyTransformedText(text.ToLowerInvariant(), _loc.Get("Toast_Copied"))
            ));
        }

        var cfg = _settings.Current;
        if (cfg.EnableCaseConverter && CaseConverter.IsConvertible(trimmed))
        {
            var words = CaseConverter.SplitWords(trimmed);
            string camel = CaseConverter.ToCamelCase(words);
            string snake = CaseConverter.ToSnakeCase(words);
            string pascal = CaseConverter.ToPascalCase(words);
            string kebab = CaseConverter.ToKebabCase(words);
            string constant = CaseConverter.ToConstantCase(words);

            if (!string.Equals(camel, trimmed, StringComparison.Ordinal))
                actions.Add(new("Action_ToCamelCase", "camelCase", "Case24", _loc.Get("Action_ToCamelCase_Desc"), () => _executor.CopyTransformedText(camel, _loc.Get("Toast_Copied")), Subtitle: camel));
            if (!string.Equals(snake, trimmed, StringComparison.Ordinal))
                actions.Add(new("Action_ToSnakeCase", "snake_case", "Case24", _loc.Get("Action_ToSnakeCase_Desc"), () => _executor.CopyTransformedText(snake, _loc.Get("Toast_Copied")), Subtitle: snake));
            if (!string.Equals(kebab, trimmed, StringComparison.Ordinal))
                actions.Add(new("Action_ToKebabCase", "kebab-case", "Case24", _loc.Get("Action_ToKebabCase_Desc"), () => _executor.CopyTransformedText(kebab, _loc.Get("Toast_Copied")), Subtitle: kebab));
            if (!string.Equals(pascal, trimmed, StringComparison.Ordinal))
                actions.Add(new("Action_ToPascalCase", "PascalCase", "Case24", _loc.Get("Action_ToPascalCase_Desc"), () => _executor.CopyTransformedText(pascal, _loc.Get("Toast_Copied")), Subtitle: pascal));
            if (!string.Equals(constant, trimmed, StringComparison.Ordinal))
                actions.Add(new("Action_ToConstantCase", "CONSTANT_CASE", "Case24", _loc.Get("Action_ToConstantCase_Desc"), () => _executor.CopyTransformedText(constant, _loc.Get("Toast_Copied")), Subtitle: constant));
        }

        string cleanSnippet = HistoryService.NormalizeSingleLine(trimmed);
        string snippet = cleanSnippet.Length > 180 ? cleanSnippet[..180] + "…" : cleanSnippet;

        return new DetectionResult(
            Type: ClipDataType.PlainText,
            RawData: text,
            PreviewTitle: _loc.Get("Type_PlainText"),
            PreviewSubtitle: _loc.Get("Sub_Text_Stats", charCount, wordCount),
            PreviewBody: snippet,
            AvailableActions: actions,
            BadgeText: "Text"
        );
    }

    private static byte ParseCssAlpha(string alphaStr)
    {
        bool isPct = alphaStr.EndsWith('%');
        double val = double.Parse(isPct ? alphaStr[..^1] : alphaStr, CultureInfo.InvariantCulture);
        if (isPct) val = Math.Clamp(val, 0.0, 100.0) / 100.0;
        return (byte)Math.Clamp(Math.Round(val * 255.0), 0, 255);
    }

    private static int CountLines(string text)
    {
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lines++;
        }
        return lines;
    }

    private static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                words++;
                inWord = true;
            }
        }
        return words;
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static string NormalizeIndentation(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var indentationWidths = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(GetLeadingWhitespaceWidth)
            .ToList();

        if (indentationWidths.Count == 0) return code;

        int baseIndent = indentationWidths.Min();
        var relativeIndents = indentationWidths
            .Select(width => width - baseIndent)
            .Where(width => width > 0)
            .ToList();
        int indentUnit = relativeIndents.Count == 0 ? 2 : relativeIndents.Aggregate(GCD);
        indentUnit = Math.Clamp(indentUnit, 1, 8);

        var resultLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                resultLines.Add(string.Empty);
                continue;
            }

            int leadingWhitespaceLength = 0;
            while (leadingWhitespaceLength < line.Length && (line[leadingWhitespaceLength] == ' ' || line[leadingWhitespaceLength] == '\t'))
            {
                leadingWhitespaceLength++;
            }

            int relativeIndent = Math.Max(0, GetLeadingWhitespaceWidth(line) - baseIndent);
            int indentationLevel = (int)Math.Round(relativeIndent / (double)indentUnit, MidpointRounding.AwayFromZero);
            resultLines.Add(new string(' ', indentationLevel * 2) + line[leadingWhitespaceLength..]);
        }
        return string.Join(Environment.NewLine, resultLines);
    }

    private static int GetLeadingWhitespaceWidth(string line)
    {
        int width = 0;
        foreach (char character in line)
        {
            if (character == ' ') width++;
            else if (character == '\t') width += 2;
            else break;
        }
        return width;
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

            if (max == rd) h = ((gd - bd) / delta) + (gd < bd ? 6 : 0);
            else if (max == gd) h = ((bd - rd) / delta) + 2;
            else h = ((rd - gd) / delta) + 4;

            h *= 60;
        }

        return (h, s, l);
    }

    private static (byte r, byte g, byte b) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = l - c / 2.0;

        double r1, g1, b1;
        if (h < 60) (r1, g1, b1) = (c, x, 0);
        else if (h < 120) (r1, g1, b1) = (x, c, 0);
        else if (h < 180) (r1, g1, b1) = (0, c, x);
        else if (h < 240) (r1, g1, b1) = (0, x, c);
        else if (h < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        return (
            (byte)Math.Clamp(Math.Round((r1 + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g1 + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b1 + m) * 255), 0, 255)
        );
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

    private DetectionResult? TryDetectJwt(string text)
    {
        try
        {
            var parts = text.Split('.');
            if (parts.Length != 3) return null;
            if (parts[0].Length < 4 || parts[1].Length < 4) return null;

            string headerJson = DecodeBase64Url(parts[0]);
            string payloadJson = DecodeBase64Url(parts[1]);

            using var payloadDoc = JsonDocument.Parse(payloadJson);
            using var headerDoc = JsonDocument.Parse(headerJson);

            string formattedPayload = JsonSerializer.Serialize(payloadDoc.RootElement, IndentedJsonOptions);
            string formattedHeader = JsonSerializer.Serialize(headerDoc.RootElement, IndentedJsonOptions);

            // Inspect exp / sub claims for immediate developer insight
            string subtitle = _loc.Get("Jwt_DecodedClaims");
            if (payloadDoc.RootElement.ValueKind == JsonValueKind.Object &&
                payloadDoc.RootElement.TryGetProperty("exp", out var expProp) &&
                expProp.ValueKind == JsonValueKind.Number &&
                expProp.TryGetInt64(out long expSec))
            {
                var expDto = DateTimeOffset.FromUnixTimeSeconds(expSec).ToLocalTime();
                string expFormatted = expDto.ToString("yyyy-MM-dd HH:mm");
                subtitle = expDto < DateTimeOffset.Now
                    ? _loc.Get("Jwt_Expired", expFormatted)
                    : _loc.Get("Jwt_ValidUntil", expFormatted);
            }

            var actions = new List<ActionItem>
            {
                new(
                    "Action_CopyJwtPayload",
                    _loc.Get("Action_CopyJwtPayload"),
                    "Key24",
                    _loc.Get("Action_CopyJwtPayload_Desc"),
                    () => _executor.CopyTransformedText(formattedPayload, _loc.Get("Toast_Copied")),
                    IsPrimary: true,
                    Subtitle: _loc.Get("Sub_Jwt_Claims")
                ),
                new(
                    "Action_CopyJwtHeader",
                    _loc.Get("Action_CopyJwtHeader"),
                    "Code24",
                    _loc.Get("Action_CopyJwtHeader_Desc"),
                    () => _executor.CopyTransformedText(formattedHeader, _loc.Get("Toast_Copied")),
                    Subtitle: _loc.Get("Sub_Jwt_Header")
                )
            };

            string snippet = formattedPayload.Length > 180 ? formattedPayload[..180] + "…" : formattedPayload;

            return new DetectionResult(
                Type: ClipDataType.Json,
                RawData: text,
                PreviewTitle: _loc.Get("Type_Jwt"),
                PreviewSubtitle: subtitle,
                PreviewBody: snippet,
                AvailableActions: actions,
                BadgeText: "JWT"
            );
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeBase64Url(string input)
    {
        string output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        byte[] bytes = Convert.FromBase64String(output);
        return Encoding.UTF8.GetString(bytes);
    }
}
