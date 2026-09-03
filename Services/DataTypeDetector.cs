using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

    private readonly ActionExecutor _executor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly SettingsService _settings;

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorRegex();

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

            // Structured parsers can allocate heavily. Long text is still
            // useful as plain text, but should not freeze the application by
            // being parsed as JSON, Base64, or a table.
            if (trimmed.Length > MaxStructuredTextLength)
            {
                return cfg.DetectPlainText ? DetectPlainText(text) : null;
            }

            // 2a. HEX Color
            if (cfg.DetectHexColor && HexColorRegex().IsMatch(trimmed))
            {
                var colorResult = TryDetectHexColor(trimmed);
                if (colorResult != null) return colorResult;
            }

            // 2b. Unix Timestamp
            if (cfg.DetectTimestamp && TimestampRegex().IsMatch(trimmed))
            {
                var tsResult = TryDetectTimestamp(trimmed);
                if (tsResult != null) return tsResult;
            }

            // 2c. JSON
            if (cfg.DetectJson && trimmed.Length <= MaxJsonLength &&
                ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
            {
                var jsonResult = TryDetectJson(trimmed, text);
                if (jsonResult != null) return jsonResult;
            }

            // 2d. URL
            if (cfg.DetectUrl && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return DetectUrl(uri, trimmed);
            }

            // 2e. Email address
            if (cfg.DetectEmail && EmailRegex().Match(trimmed) is { Success: true } emailMatch)
            {
                return DetectEmail(trimmed, emailMatch.Groups["local"].Value, emailMatch.Groups["domain"].Value);
            }

            // 2f. Base64
            if (cfg.DetectBase64 && trimmed.Length <= MaxBase64Length &&
                (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || IsBase64String(trimmed)))
            {
                var b64Result = TryDetectBase64(trimmed);
                if (b64Result != null) return b64Result;
            }

            // 2f. Table Data (CSV / TSV)
            if (cfg.DetectTable)
            {
                var tableResult = TryDetectTableData(trimmed);
                if (tableResult != null) return tableResult;
            }

            // 2g. Code Snippet
            if (cfg.DetectCode && IsCodeSnippet(text))
            {
                return DetectCodeSnippet(text);
            }

            // 2h. Fallback: Plain Text
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

    private DetectionResult? TryDetectTimestamp(string text)
    {
        try
        {
            if (!long.TryParse(text, out long val)) return null;

            DateTimeOffset dto;
            if (text.Length == 10) // seconds
            {
                dto = DateTimeOffset.FromUnixTimeSeconds(val);
            }
            else if (text.Length == 13) // milliseconds
            {
                dto = DateTimeOffset.FromUnixTimeMilliseconds(val);
            }
            else
            {
                return null;
            }

            // Reasonableness check: 1990 <= year <= 2100
            if (dto.Year < 1990 || dto.Year > 2100) return null;

            var local = dto.ToLocalTime();
            string localStr = local.ToString("yyyy-MM-dd HH:mm:ss");
            string isoStr = dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string subtitle = $"UTC: {dto.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z ({local.ToString("zzz")})";

            var actions = new List<ActionItem>
            {
                new(
                    "Action_CopyLocalDate",
                    _loc.Get("Action_CopyLocalDate"),
                    "Calendar24",
                    _loc.Get("Action_CopyLocalDate_Desc"),
                    () => _executor.CopyText(localStr, "Toast_Copied")
                ),
                new(
                    "Action_CopyIsoDate",
                    _loc.Get("Action_CopyIsoDate"),
                    "Globe24",
                    _loc.Get("Action_CopyIsoDate_Desc"),
                    () => _executor.CopyText(isoStr, "Toast_Copied")
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

            byte[] bytes = Convert.FromBase64String(payload.Trim());
            if (bytes.Length < 4) return null;

            bool isImage = (mimeType != null && mimeType.StartsWith("image/")) || IsImageBytes(bytes);

            var actions = new List<ActionItem>();

            if (isImage)
            {
                actions.Add(new(
                    "Action_CopyDecodedImage",
                    _loc.Get("Action_CopyDecodedImage"),
                    "Image24",
                    _loc.Get("Action_CopyDecodedImage_Desc"),
                    () => _executor.CopyBase64Image(bytes)
                ));
            }

            // Also check if valid text
            string? decodedText = TryDecodeUtf8Text(bytes);
            bool isPrintableText = decodedText is { Length: > 0 } &&
                decodedText.All(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t');

            if (isPrintableText && decodedText != null)
            {
                actions.Add(new(
                    "Action_DecodeBase64",
                    _loc.Get("Action_DecodeBase64"),
                    "DocumentText24",
                    _loc.Get("Action_DecodeBase64_Desc"),
                    () => _executor.CopyText(decodedText, "Toast_Base64Decoded")
                ));
            }

            if (actions.Count == 0) return null;

            string title = isImage ? "Base64 Image" : "Base64 Text";
            string subtitle = $"{bytes.Length} bytes decoded";
            string snippet = isPrintableText && decodedText != null
                ? (decodedText.Length > 120 ? decodedText[..120] + "..." : decodedText)
                : $"Decoded binary data ({bytes.Length} bytes)";

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

    private static bool IsBase64String(string s)
    {
        if (s.Length < 16 || s.Length % 4 != 0) return false;
        if (s.Contains(' ') || s.Contains('\n') || s.Contains('\r')) return false;

        // Fast character validation
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = (c >= 'A' && c <= 'Z') ||
                      (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') ||
                      c == '+' || c == '/' || c == '=';
            if (!ok) return false;
        }
        return true;
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
        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // GIF: 47 49 46
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        // BMP: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        // WebP: RIFF ... WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
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

            // A table with irregular rows is more likely prose or malformed
            // source data than a conversion candidate.
            if (rows.Any(r => r.Length != colCount)) return null;

            // Generate Markdown table
            var mdSb = new StringBuilder();
            mdSb.AppendLine("| " + string.Join(" | ", rows[0].Select(EscapeMarkdownCell)) + " |");
            mdSb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", colCount)) + " |");
            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                mdSb.AppendLine("| " + string.Join(" | ", r.Select(EscapeMarkdownCell)) + " |");
            }
            string markdownTable = mdSb.ToString().TrimEnd();

            // Generate JSON Array
            var jsonList = new List<Dictionary<string, string>>();
            var headers = MakeUniqueHeaders(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                var dict = new Dictionary<string, string>();
                for (int j = 0; j < headers.Length; j++)
                {
                    string val = j < rows[i].Length ? rows[i][j] : "";
                    dict[headers[j]] = val;
                }
                jsonList.Add(dict);
            }
            string jsonArray = JsonSerializer.Serialize(jsonList, new JsonSerializerOptions { WriteIndented = true });

            var actions = new List<ActionItem>
            {
                new(
                    "Action_ToMarkdownTable",
                    _loc.Get("Action_ToMarkdownTable"),
                    "Table24",
                    _loc.Get("Action_ToMarkdownTable_Desc"),
                    () => _executor.CopyText(markdownTable, "Toast_MarkdownTableCopied")
                ),
                new(
                    "Action_ToJsonArray",
                    _loc.Get("Action_ToJsonArray"),
                    "Code24",
                    _loc.Get("Action_ToJsonArray_Desc"),
                    () => _executor.CopyText(jsonArray, "Toast_JsonArrayCopied")
                )
            };

            string subtitle = $"{rows.Count} rows × {colCount} columns ({(isTsv ? "TSV" : "CSV")})";
            string snippet = string.Join("\n", rows.Take(2).Select(row => string.Join(isTsv ? "\t" : ",", row)));
            if (snippet.Length > 180) snippet = snippet[..180] + "...";

            return new DetectionResult(
                Type: ClipDataType.TableData,
                RawData: text,
                PreviewTitle: _loc.Get("Type_TableData"),
                PreviewSubtitle: subtitle,
                PreviewBody: snippet,
                AvailableActions: actions,
                BadgeText: isTsv ? "TSV Table" : "CSV Table"
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeTsv(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
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

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '"')
            {
                if (insideQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (!insideQuotes && current == separator)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (!insideQuotes && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString().Trim());
                cell.Clear();

                if (row.Any(value => value.Length > 0))
                {
                    rows.Add(row.ToArray());
                    if (rows.Count > MaxTableRows) return null;
                }
                row.Clear();
            }
            else
            {
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
            // Action delegates run after this using scope exits, so clone the
            // element into independent storage before keeping it in a closure.
            var root = doc.RootElement.Clone();
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

    private DetectionResult DetectEmail(string emailAddress, string localPart, string domain)
    {
        var actions = new List<ActionItem>
        {
            new("Action_OpenEmail", _loc.Get("Action_OpenEmail"), "Mail24", _loc.Get("Action_OpenEmail_Desc"), () => _executor.OpenEmail(emailAddress)),
            new("Action_CopyEmailDomain", _loc.Get("Action_CopyEmailDomain"), "Link24", _loc.Get("Action_CopyEmailDomain_Desc"), () => _executor.CopyText(domain, "Toast_Copied")),
            new("Action_CopyEmailUser", _loc.Get("Action_CopyEmailUser"), "Person24", _loc.Get("Action_CopyEmailUser_Desc"), () => _executor.CopyText(localPart, "Toast_Copied"))
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
                () => _executor.CopyText(text.ToUpperInvariant(), "Toast_Copied")
            ),
            new(
                "Action_LowerCase",
                _loc.Get("Action_LowerCase"),
                "TextCaseLowercase24",
                _loc.Get("Action_LowerCase_Desc"),
                () => _executor.CopyText(text.ToLowerInvariant(), "Toast_Copied")
            )
        };

        string cleanSnippet = Regex.Replace(trimmed, @"[\r\n\t]+", " ");
        string snippet = cleanSnippet.Length > 180 ? cleanSnippet[..180] + "..." : cleanSnippet;

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
        var indentationWidths = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(GetLeadingWhitespaceWidth)
            .ToList();

        if (indentationWidths.Count == 0)
        {
            return code;
        }

        int baseIndent = indentationWidths.Min();
        var relativeIndents = indentationWidths
            .Select(width => width - baseIndent)
            .Where(width => width > 0)
            .ToList();
        int indentUnit = relativeIndents.Count == 0
            ? 2
            : relativeIndents.Aggregate(GCD);

        // Avoid treating alignment whitespace (for example 37 spaces in a
        // pasted SQL query) as a logical indentation level.
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
            if (character == ' ')
            {
                width++;
            }
            else if (character == '\t')
            {
                width += 2;
            }
            else
            {
                break;
            }
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
