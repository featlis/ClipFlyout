using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClipFlyout.Services;

public enum AppLanguage
{
    Auto,
    Japanese,
    English
}

/// <summary>
/// Centralized multi-language / i18n service supporting Japanese and English.
/// </summary>
public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event Action? LanguageChanged;

    private AppLanguage _currentLanguage = AppLanguage.Auto;
    private bool _isJapanese;

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                UpdateLanguageResolution();
                LanguageChanged?.Invoke();
            }
        }
    }

    public bool IsJapanese => _isJapanese;

    public LocalizationService()
    {
        UpdateLanguageResolution();
    }

    public void UpdateLanguageResolution()
    {
        if (_currentLanguage == AppLanguage.Japanese)
        {
            _isJapanese = true;
        }
        else if (_currentLanguage == AppLanguage.English)
        {
            _isJapanese = false;
        }
        else
        {
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            _isJapanese = lang == "ja";
        }
    }

    public string FormatRelativeTime(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;
        if (elapsed.TotalSeconds < 45) return Get("Flyout_Time_JustNow");
        if (elapsed.TotalMinutes < 60) return Get("Flyout_Time_MinutesAgo", Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed.TotalHours < 24) return Get("Flyout_Time_HoursAgo", Math.Max(1, (int)elapsed.TotalHours));
        return Get("Flyout_Time_DaysAgo", Math.Max(1, (int)elapsed.TotalDays));
    }

    public string Get(string key, params object[] args)
    {
        if (!_strings.TryGetValue(key, out var translation))
        {
            return key;
        }

        string val = _isJapanese ? translation.Ja : translation.En;
        if (args.Length > 0)
        {
            try
            {
                return string.Format(val, args);
            }
            catch
            {
                return val;
            }
        }
        return val;
    }

    private record Translation(string Ja, string En);

    private readonly Dictionary<string, Translation> _strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Type Names
        { "Type_HexColor", new("カラーコード", "Color Code") },
        { "Type_Json", new("JSON データ", "JSON Data") },
        { "Type_Url", new("Web URL", "Web URL") },
        { "Type_Email", new("メールアドレス", "Email Address") },
        { "Type_Code", new("コードスニペット", "Code Snippet") },
        { "Type_Image", new("画像", "Image") },
        { "Type_PlainText", new("プレーンテキスト", "Plain Text") },
        { "Type_Timestamp", new("Unix タイムスタンプ", "Unix Timestamp") },
        { "Type_IsoDateTime", new("ISO 8601 日時", "ISO 8601 Date/Time") },
        { "Type_Base64", new("Base64 データ", "Base64 Data") },
        { "Type_Base64Image", new("Base64 画像", "Base64 Image") },
        { "Type_Base64Text", new("Base64 テキスト", "Base64 Text") },
        { "Type_TableData", new("表データ (CSV/TSV)", "Table Data (CSV/TSV)") },
        { "Type_FileDrop", new("ファイル / フォルダ", "Files / Folders") },
        { "Type_FilePath", new("ファイルパス", "File Path") },
        { "Type_MathExpression", new("数式 / 進数変換", "Math / Base Conversion") },
        { "Type_Uuid", new("UUID / GUID", "UUID / GUID") },
        { "Type_Jwt", new("JWT トークン", "JWT Token") },

        // Flyout Header & Chrome
        { "Type_HexColor_Desc", new("カラーコード", "Color Code") },
        { "Flyout_Time_JustNow", new("たった今", "Just now") },
        { "Flyout_Time_MinutesAgo", new("{0}分前", "{0}m ago") },
        { "Flyout_Time_HoursAgo", new("{0}時間前", "{0}h ago") },
        { "Flyout_Time_DaysAgo", new("{0}日前", "{0}d ago") },
        { "Flyout_Recent_Header", new("最近の履歴", "Recent") },
        { "Flyout_Pinned_Header", new("ピン留め", "Pinned") },
        { "Flyout_Pin_Tooltip", new("ピン留め / 解除", "Pin / Unpin") },
        { "Flyout_Search_Tooltip", new("履歴を検索", "Search history") },
        { "Flyout_Search_Placeholder", new("履歴を絞り込み...", "Filter history...") },
        { "Flyout_ClearHistory", new("クリア", "Clear") },
        { "Flyout_Settings_Tooltip", new("設定を開く", "Open Settings") },
        { "Flyout_Close_Tooltip", new("閉じる (Esc)", "Close (Esc)") },
        { "Flyout_KeyboardHint", new("↑↓ 選択 • Enter / 1-9 実行 • Esc 閉じる", "↑↓ Select • Enter / 1-9 Run • Esc Close") },

        // Actions: Color
        { "Action_CopyHex", new("HEX をコピー", "Copy HEX") },
        { "Action_CopyHex_Desc", new("HEX カラーコードをコピー", "Copy color HEX code") },
        { "Action_ConvertColor", new("形式を変換", "Convert") },
        { "Action_ConvertColor_Desc", new("RGB, HSL, RGBA", "RGB, HSL, RGBA") },
        { "Action_CopyRgb", new("RGB をコピー", "Copy RGB") },
        { "Action_CopyRgb_Desc", new("RGB 形式 (rgb(r, g, b)) でコピー", "Copy in rgb(r, g, b) format") },
        { "Action_CopyHsl", new("HSL をコピー", "Copy HSL") },
        { "Action_CopyHsl_Desc", new("HSL 形式 (hsl(h, s%, l%)) でコピー", "Copy in hsl(h, s%, l%) format") },
        { "Action_CopyRgba", new("RGBA をコピー", "Copy RGBA") },
        { "Action_CopyRgba_Desc", new("RGBA 形式 (rgba(r, g, b, a)) でコピー", "Copy in rgba(r, g, b, a) format") },

        // Actions: JSON & JWT
        { "Action_FormatJson", new("整形してコピー", "Format & Copy") },
        { "Action_FormatJson_Desc", new("2スペースインデントで見やすく整形", "Prettify JSON with 2-space indentation") },
        { "Action_MinifyJson", new("1行化してコピー", "Minify & Copy") },
        { "Action_MinifyJson_Desc", new("余分な空白を除去して1行に圧縮", "Remove whitespace and minify to a single line") },
        { "Action_CopyJwtPayload", new("JWT ペイロードをコピー", "Copy JWT Payload") },
        { "Action_CopyJwtPayload_Desc", new("デコードしたクレーム JSON を整形コピー", "Copy formatted JWT claims JSON") },
        { "Action_CopyJwtHeader", new("JWT ヘッダーをコピー", "Copy JWT Header") },
        { "Action_CopyJwtHeader_Desc", new("JWT ヘッダー JSON をコピー", "Copy JWT header JSON") },
        { "Jwt_Expired", new("期限切れ ({0})", "Expired ({0})") },
        { "Jwt_ValidUntil", new("有効期限: {0}", "Valid until {0}") },
        { "Jwt_DecodedClaims", new("デコード済みクレーム", "Decoded JWT Claims") },

        // Actions: URL & Email
        { "Action_OpenBrowser", new("ブラウザで開く", "Open in Browser") },
        { "Action_OpenBrowser_Desc", new("既定のブラウザでリンク先を開く", "Open link in default web browser") },
        { "Action_CopyQrCode", new("QR 画像をコピー", "Copy QR Image") },
        { "Action_CopyQrCode_Desc", new("QR コード画像を生成してクリップボードへコピー", "Generate QR code image and copy") },
        { "Action_CopyDomain", new("ドメインをコピー", "Copy Domain") },
        { "Action_CopyDomain_Desc", new("ホスト名部分のみをコピー", "Copy host domain name only") },
        { "Action_CopyCleanUrl", new("クリーン URL をコピー", "Copy Clean URL") },
        { "Action_CopyCleanUrl_Desc", new("トラッキングパラメータを除去してコピー", "Strip UTM and tracking parameters") },
        { "Action_DecodeUrl", new("URL デコード", "Decode URL") },
        { "Action_DecodeUrl_Desc", new("パーセントエンコード文字列を復号してコピー", "Decode percent-encoded characters") },
        { "Action_OpenEmail", new("メールを作成", "Compose Email") },
        { "Action_OpenEmail_Desc", new("既定のメールアプリで新規メッセージを作成", "Open new message in default mail app") },
        { "Action_CopyEmailDomain", new("ドメインをコピー", "Copy Domain") },
        { "Action_CopyEmailDomain_Desc", new("@ より後ろのドメイン部分をコピー", "Copy email domain part") },
        { "Action_CopyEmailUser", new("ユーザー名をコピー", "Copy Username") },
        { "Action_CopyEmailUser_Desc", new("@ より前のローカル部分をコピー", "Copy local username part") },

        // Actions: Code
        { "Action_AdjustIndent", new("インデント調整", "Normalize Indent") },
        { "Action_AdjustIndent_Desc", new("インデントを半角スペース2文字に正規化", "Normalize indentation to 2 spaces") },
        { "Action_EscapeHtml", new("HTML エスケープ", "Escape HTML") },
        { "Action_EscapeHtml_Desc", new("<, >, &, \" を HTML エンティティに変換", "Encode <, >, &, \" into HTML entities") },

        // Actions: Image
        { "Action_SavePng", new("画像として保存", "Save Image") },
        { "Action_SavePng_Desc", new("クリップボード画像を PNG / JPG / BMP で保存", "Save clipboard image to PNG, JPG, or BMP") },
        { "Action_CopyImageInfo", new("解像度情報をコピー", "Copy Image Info") },
        { "Action_CopyImageInfo_Desc", new("解像度とアスペクト比をコピー", "Copy dimensions and aspect ratio") },

        // Actions: FileDrop
        { "Action_CopyFilePaths", new("パスをコピー", "Copy Path(s)") },
        { "Action_CopyFilePaths_Desc", new("選択されたファイル・フォルダのフルパスをコピー", "Copy full path(s) of copied files/folders") },
        { "Action_CopyQuotedPaths", new("引用符付きパスをコピー", "Copy Quoted Path(s)") },
        { "Action_CopyQuotedPaths_Desc", new("ターミナル用に \"...\" で囲んでコピー", "Wrap paths in quotes for terminal usage") },
        { "Action_CopyFileNames", new("ファイル名をコピー", "Copy File Name(s)") },
        { "Action_CopyFileNames_Desc", new("ファイル・フォルダ名のみをコピー", "Copy file/folder names only") },
        { "Action_OpenFolder", new("エクスプローラーで開く", "Show in Explorer") },
        { "Action_OpenFolder_Desc", new("ファイルの場所をエクスプローラーで開く", "Reveal item in File Explorer") },

        // Actions: Plain Text
        { "Action_TrimWhitespace", new("前後空白をトリム", "Trim Whitespace") },
        { "Action_TrimWhitespace_Desc", new("前後の余分な空白・改行を除去してコピー", "Remove leading and trailing whitespace") },
        { "Action_TextStats", new("文字数カウントをコピー", "Copy Text Stats") },
        { "Action_TextStats_Desc", new("文字数・単語数・行数をコピー", "Copy character, word, and line counts") },
        { "Action_UpperCase", new("大文字に変換", "UPPERCASE") },
        { "Action_UpperCase_Desc", new("英字をすべて大文字に変換", "Convert letters to UPPERCASE") },
        { "Action_LowerCase", new("小文字に変換", "lowercase") },
        { "Action_LowerCase_Desc", new("英字をすべて小文字に変換", "Convert letters to lowercase") },
        { "Action_ToCamelCase_Desc", new("camelCase に変換してコピー", "Convert to camelCase") },
        { "Action_ToSnakeCase_Desc", new("snake_case に変換してコピー", "Convert to snake_case") },
        { "Action_ToKebabCase_Desc", new("kebab-case に変換してコピー", "Convert to kebab-case") },
        { "Action_ToPascalCase_Desc", new("PascalCase に変換してコピー", "Convert to PascalCase") },
        { "Action_ToConstantCase_Desc", new("CONSTANT_CASE に変換してコピー", "Convert to CONSTANT_CASE") },

        // Localized Detector Subtitles & Labels
        { "Sub_FileDrop_Items", new("{0} 件のアイテム", "{0} items") },
        { "Sub_FileDrop_Paths", new("{0} 件のパス", "{0} paths") },
        { "Sub_FileDrop_More", new("+{0} 件", "+{0} more") },
        { "Badge_FileSingle", new("ファイル", "File") },
        { "Badge_FileMulti", new("{0} 件", "{0} Files") },
        { "Sub_Table_Dims", new("{0} 行 × {1} 列 ({2})", "{0} rows × {1} cols ({2})") },
        { "Sub_Table_Objects", new("{0} 件のオブジェクト", "{0} objects") },
        { "Sub_Json_Object", new("オブジェクト • {0} プロパティ", "Object • {0} properties") },
        { "Sub_Json_Array", new("配列 • {0} 要素", "Array • {0} items") },
        { "Sub_Json_Value", new("JSON 値", "JSON Value") },
        { "Sub_Json_Pretty", new("整形 (2スペース)", "Pretty (2-space)") },
        { "Sub_Json_Minify", new("1行圧縮", "Single line") },
        { "Sub_CleanUrl", new("追跡パラメータ除去", "Strip tracking params") },
        { "Sub_Code_Indent", new("2スペースインデント", "2-space indent") },
        { "Sub_Code_Stats", new("{0} 行 • {1} 文字", "{0} lines • {1} chars") },
        { "Sub_Text_Stats", new("{0} 文字 • {1} 単語", "{0} chars • {1} words") },
        { "Sub_Text_FullStats", new("{0} 文字 • {1} 単語 • {2} 行", "{0} chars • {1} words • {2} lines") },
        { "Sub_Text_LargeStats", new("{0:N0} 文字 • {1:N0} 行", "{0:N0} chars • {1:N0} lines") },
        { "Sub_Jwt_Claims", new("JSON クレーム", "JSON Claims") },
        { "Sub_Jwt_Header", new("ヘッダー JSON", "Header JSON") },

        // Actions: Timestamp & ISO DateTime
        { "Action_CopyLocalDate", new("ローカル日時をコピー", "Copy Local Time") },
        { "Action_CopyLocalDate_Desc", new("現在のタイムゾーンで日時をコピー", "Format and copy in local timezone") },
        { "Action_CopyIsoDate", new("ISO 8601 をコピー", "Copy ISO 8601") },
        { "Action_CopyIsoDate_Desc", new("UTC ISO 8601 形式 (YYYY-MM-DDTHH:mm:ssZ) でコピー", "Copy in UTC ISO 8601 format") },
        { "Action_CopyUnixSeconds", new("Unix 秒をコピー", "Copy Unix Seconds") },
        { "Action_CopyUnixSeconds_Desc", new("10桁の Unix エポック秒に変換してコピー", "Convert to 10-digit Unix epoch seconds") },
        { "Action_CopyUnixMillis", new("Unix ミリ秒をコピー", "Copy Unix Milliseconds") },
        { "Action_CopyUnixMillis_Desc", new("13桁の Unix エポックミリ秒に変換してコピー", "Convert to 13-digit Unix epoch milliseconds") },
        { "Action_CopyCurrentTimestamp", new("現在の Epoch をコピー", "Copy Current Epoch") },
        { "Action_CopyCurrentTimestamp_Desc", new("現在時刻の Unix 秒タイムスタンプをコピー", "Copy current Unix timestamp in seconds") },

        // Actions: Base64
        { "Action_DecodeBase64", new("デコードしてコピー", "Decode & Copy") },
        { "Action_DecodeBase64_Desc", new("Base64 文字列をテキストに復号してコピー", "Decode Base64 string to UTF-8 text") },
        { "Action_CopyDecodedImage", new("画像としてコピー", "Copy as Image") },
        { "Action_CopyDecodedImage_Desc", new("Base64 画像をクリップボードへ展開", "Decode image data to clipboard") },

        // Actions: Table Data
        { "Action_ToMarkdownTable", new("Markdown 表に変換", "To Markdown Table") },
        { "Action_ToMarkdownTable_Desc", new("TSV/CSV を Markdown テーブルに整形してコピー", "Format table into Markdown syntax") },
        { "Action_ToJsonArray", new("JSON 配列に変換", "To JSON Array") },
        { "Action_ToJsonArray_Desc", new("ヘッダー行をキーとした JSON 配列へ変換", "Convert table rows to JSON objects") },

        // Actions: Generic & Transform
        { "Action_Paste", new("アクティブなアプリへ貼り付け", "Paste to Active App") },
        { "Action_Paste_Desc", new("変換結果をそのまま貼り付け (Ctrl+V)", "Paste result directly (Ctrl+V)") },
        { "Action_Copy", new("コピー", "Copy") },
        { "Action_Copy_Desc", new("クリップボードへコピー", "Copy to clipboard") },
        { "Transform_Title", new("変換完了", "Transformed") },
        { "Badge_Transformed", new("変換済", "Transformed") },

        // Toasts
        { "Toast_Copied", new("クリップボードにコピーしました", "Copied to clipboard") },
        { "Toast_FormattedJsonCopied", new("整形済み JSON をコピーしました", "Prettified JSON copied") },
        { "Toast_MinifiedJsonCopied", new("1行化 JSON をコピーしました", "Minified JSON copied") },
        { "Toast_QrCopied", new("QR コード画像をコピーしました", "QR code image copied") },
        { "Toast_ImageSaved", new("画像を保存しました: {0}", "Image saved: {0}") },
        { "Toast_BrowserOpened", new("ブラウザで開きました", "Opened in browser") },
        { "Toast_EmailOpened", new("メールアプリを開きました", "Opened mail app") },
        { "Toast_FolderOpened", new("エクスプローラーで開きました", "Opened in Explorer") },
        { "Toast_InvalidUrl", new("開けない URL 形式です", "Unsupported URL scheme") },
        { "Toast_Base64Decoded", new("Base64 をデコードしてコピーしました", "Base64 decoded & copied") },
        { "Toast_MarkdownTableCopied", new("Markdown 表をコピーしました", "Markdown table copied") },
        { "Toast_JsonArrayCopied", new("JSON 配列をコピーしました", "JSON array copied") },
        { "Toast_Pasted", new("貼り付けました", "Pasted to active app") },
        { "Toast_PasteBlocked", new("貼り付けがブロックされました（コピー済み）", "Paste blocked by target app (copied)") },
        { "Toast_CleanUrlCopied", new("クリーン URL をコピーしました", "Clean URL copied") },
        { "Toast_UrlDecoded", new("URL をデコードしてコピーしました", "URL decoded & copied") },
        { "Toast_OcrCopied", new("画像から抽出したテキストをコピーしました", "Extracted OCR text copied") },
        { "Toast_OcrEmpty", new("画像からテキストを検出できませんでした", "No readable text found in image") },
        { "Action_ExtractOcr", new("画像から文字起こし (OCR)", "Extract Text (OCR)") },
        { "Action_ExtractOcr_Desc", new("OS 内蔵 OCR で画像内の日本語・英語テキストをローカル抽出", "Extract text locally using Windows on-device OCR") },
        { "Toast_TerminalOpened", new("ターミナルを開きました", "Opened in Terminal") },
        { "Toast_TerminalDirMissing", new("開けるディレクトリが見つかりませんでした", "Target directory does not exist") },
        { "Toast_Sha256Copied", new("SHA-256 ハッシュをコピーしました", "SHA-256 hash copied") },
        { "Toast_Sha256Unavailable", new("ファイルの SHA-256 を計算できませんでした", "Could not compute file SHA-256") },
        { "Toast_Base64Encoded", new("Base64 エンコード結果をコピーしました", "Base64 encoded text copied") },
        { "Toast_UuidGenerated", new("新しい UUID を生成してコピーしました", "Generated and copied new UUID") },
        { "Sub_FilePath_Exists", new("{0} • 存在確認済み", "{0} • Path exists") },
        { "Sub_Math_Expr", new("式: {0}", "Expr: {0}") },
        { "Action_CopyWindowsPath", new("Windows パス (\\) に変換", "Convert to Windows Path (\\)") },
        { "Action_CopyWindowsPath_Desc", new("バックスラッシュ区切りの Windows パスに変換してコピー", "Convert to backslash-delimited Windows path") },
        { "Action_CopyForwardPath", new("スラッシュ区切り (/) に変換", "Convert to Forward Slashes (/)") },
        { "Action_CopyForwardPath_Desc", new("URL やコード用に / 区切りパスへ変換してコピー", "Convert backslashes to forward slashes") },
        { "Action_CopyWslPath", new("WSL パス (/mnt/...) に変換", "Convert to WSL Path (/mnt/...)") },
        { "Action_CopyWslPath_Desc", new("WSL 環境用の /mnt/ 形式パスへ変換してコピー", "Convert drive letter path to WSL /mnt/ format") },
        { "Action_CopyEscapedPath", new("エスケープ済み (\\\\) パスをコピー", "Copy Escaped Path (\\\\)") },
        { "Action_CopyEscapedPath_Desc", new("JSON / C# / 文字列リテラル用に \\\\ でエスケープ", "Double backslashes for JSON or string literals") },
        { "Action_OpenTerminal", new("ターミナルで開く", "Open in Terminal") },
        { "Action_OpenTerminal_Desc", new("このパスのフォルダで Windows Terminal / PowerShell を起動", "Launch terminal at this folder") },
        { "Action_CopyMathResult", new("計算結果 (10進数) をコピー", "Copy Result (Decimal)") },
        { "Action_CopyMathResult_Desc", new("計算結果をクリップボードへコピー", "Copy calculated numeric value") },
        { "Action_CopyHexValue", new("16進数 (0x...) をコピー", "Copy Hexadecimal (0x...)") },
        { "Action_CopyHexValue_Desc", new("16進数表記をコピー", "Copy 0x hexadecimal value") },
        { "Action_CopyBinaryValue", new("2進数 (0b...) をコピー", "Copy Binary (0b...)") },
        { "Action_CopyBinaryValue_Desc", new("2進数表記をコピー", "Copy 0b binary value") },
        { "Action_CopyUuidLower", new("小文字 UUID をコピー", "Copy Lowercase UUID") },
        { "Action_CopyUuidLower_Desc", new("ハイフン付き小文字フォーマットでコピー", "Copy standard lowercase hyphenated UUID") },
        { "Action_CopyUuidUpper", new("大文字 UUID をコピー", "Copy Uppercase UUID") },
        { "Action_CopyUuidUpper_Desc", new("ハイフン付き大文字フォーマットでコピー", "Copy uppercase hyphenated UUID") },
        { "Action_CopyUuidCompact", new("ハイフンなし UUID をコピー", "Copy Compact UUID") },
        { "Action_CopyUuidCompact_Desc", new("32桁の16進数フォーマットでコピー", "Copy 32-char compact hex UUID") },
        { "Action_NewUuidV4", new("新しい UUID v4 を生成", "Generate New UUID v4") },
        { "Action_NewUuidV4_Desc", new("ランダム UUID v4 を生成してコピー", "Generate random UUID v4") },
        { "Action_NewUuidV7", new("新しい UUID v7 を生成", "Generate New UUID v7") },
        { "Action_NewUuidV7_Desc", new("時刻順ソート可能な UUID v7 を生成してコピー", "Generate time-ordered UUID v7") },
        { "Action_CopySha256", new("SHA-256 ハッシュをコピー", "Copy SHA-256 Hash") },
        { "Action_CopySha256_Desc", new("UTF-8 テキストの SHA-256 ダイジェストをコピー", "Compute and copy SHA-256 digest") },
        { "Action_CopyFileSha256_Desc", new("ファイルの SHA-256 チェックサムを計算してコピー", "Compute and copy file SHA-256 checksum") },
        { "Action_EncodeBase64", new("Base64 にエンコード", "Encode to Base64") },
        { "Action_EncodeBase64_Desc", new("UTF-8 文字列を Base64 に変換してコピー", "Encode text as UTF-8 Base64") },
        { "Action_JoinPrevious", new("前のクリップと結合", "Join with Previous Clip") },
        { "Action_JoinPrevious_Desc", new("直前にコピーしたテキストと改行で連結してコピー", "Combine previous copied text and current clip") },
        { "Toast_ClipsJoined", new("2つのクリップを結合してコピーしました", "Joined two clips to clipboard") },
        { "Toast_SnippetSaved", new("定型文に保存しました", "Saved to snippets") },
        { "Flyout_Tab_History", new("履歴", "History") },
        { "Flyout_Tab_Snippets", new("定型文", "Snippets") },
        { "Flyout_Edit_Tooltip", new("テキストを編集", "Edit text") },
        { "Flyout_SaveSnippet_Tooltip", new("定型文として保存", "Save as snippet") },
        { "Flyout_QuickPaste_Tooltip", new("直接貼り付け", "Paste directly") },
        { "Flyout_QuickDelete_Tooltip", new("削除", "Delete") },
        { "Flyout_Edit_Copy", new("コピー", "Copy") },
        { "Flyout_Edit_Paste", new("貼り付け", "Paste") },
        { "Flyout_Edit_Save", new("定型文保存", "Save Snippet") },
        { "Snippets_Empty", new("定型文はまだありません", "No snippets yet") },
        { "Snippets_Editor_Header", new("定型文の追加 / 編集", "Add / Edit Snippet") },
        { "Snippets_Title_Placeholder", new("タイトル（省略時は本文から自動生成）", "Title (optional)") },
        { "Snippets_Content_Placeholder", new("よく使うテキスト・コマンド・署名などを入力...", "Enter frequently used text, commands, or templates...") },
        { "Snippets_Save", new("追加", "Add") },
        { "Snippets_Update", new("更新", "Update") },
        { "Snippets_CancelEdit", new("キャンセル", "Cancel") },
        { "Snippets_List_Header", new("保存済みの定型文 ({0})", "Saved Snippets ({0})") },
        { "Snippets_Edit", new("編集", "Edit") },
        { "Snippets_Copy", new("コピー", "Copy") },
        { "Snippets_Delete", new("削除", "Delete") },

        // Tray Menu & Status
        { "Tray_Settings_Flyout", new("フライアウトを開く", "Open Flyout") },
        { "Tray_Settings", new("設定...", "Settings...") },
        { "Tray_Theme", new("テーマ", "Theme") },
        { "Tray_ThemeSystem", new("システム連動", "System Default") },
        { "Tray_ThemeLight", new("ライト", "Light") },
        { "Tray_ThemeDark", new("ダーク", "Dark") },
        { "Tray_TitleActive", new("ClipFlyout - 連携中", "ClipFlyout - Active") },
        { "Tray_TitlePaused", new("ClipFlyout - 一時停止中", "ClipFlyout - Paused") },
        { "Tray_ToggleMonitoring", new("クリップボード連携を有効にする", "Clipboard Integration") },
        { "Tray_Language", new("言語 (Language)", "Language") },
        { "Tray_LangJapanese", new("日本語 (Japanese)", "日本語 (Japanese)") },
        { "Tray_LangEnglish", new("English", "English") },
        { "Tray_LangAuto", new("自動検出 (Auto)", "Auto Detect") },
        { "Tray_History", new("最近の履歴", "Recent History") },
        { "Tray_ClearHistory", new("履歴をクリア", "Clear History") },
        { "History_Empty", new("履歴はありません", "No history yet") },
        { "Tray_Snippets", new("定型文", "Snippets") },
        { "Tray_ManageSnippets", new("定型文を管理...", "Manage Snippets...") },
        { "Tray_PauseMenu", new("監視の一時停止", "Pause Monitoring") },
        { "Tray_Pause15m", new("15分間一時停止", "Pause for 15 minutes") },
        { "Tray_Pause1h", new("1時間一時停止", "Pause for 1 hour") },
        { "Tray_Resume", new("今すぐ再開", "Resume Now") },
        { "Tray_TitlePausedUntil", new("ClipFlyout - 一時停止中 (残り {0} 分)", "ClipFlyout - Paused ({0}m left)") },
        { "Tray_CheckUpdates", new("更新プログラムを確認", "Check for Updates") },
        { "Tray_Exit", new("終了", "Exit") },

        // Settings Navigation & Headers
        { "Settings_Title", new("ClipFlyout 設定", "ClipFlyout Settings") },
        { "Settings_SubTitle", new("クリップボード検出と動作のカスタマイズ", "Customize clipboard detection and behavior") },
        { "Nav_General", new("全般", "General") },
        { "Nav_Flyout", new("外観とフライアウト", "Appearance & Flyout") },
        { "Nav_Widget", new("タスクバーウィジェット", "Taskbar Widget") },
        { "Nav_Detectors", new("データ型フィルター", "Detectors") },
        { "Nav_History", new("履歴とプライバシー", "History & Privacy") },
        { "Nav_Snippets", new("定型文", "Snippets") },
        { "Nav_About", new("更新と情報", "Updates & About") },

        // Settings: General
        { "Section_General", new("全般", "General") },
        { "Setting_Monitoring", new("クリップボード連携", "Clipboard Integration") },
        { "Setting_Monitoring_Desc", new("コピー内容をローカル解析し、状況に応じたアクションを即座に提案します", "Analyze copied data locally and suggest contextual actions") },
        { "Setting_Startup", new("Windows 起動時に自動開始", "Launch on Windows Startup") },
        { "Setting_Startup_Desc", new("サインイン時にバックグラウンドで ClipFlyout を起動します", "Automatically start ClipFlyout in the background on sign-in") },
        { "Setting_EnableRecallHotkey", new("リコールショートカット ({0})", "Recall Shortcut ({0})") },
        { "Setting_EnableRecallHotkey_Desc", new("フライアウトの表示・非表示をグローバルショートカットで切り替えます", "Toggle the last flyout anytime with this shortcut") },
        { "Setting_HotkeyConflict", new("他のアプリと競合しているため {0} を登録できませんでした", "Could not register {0} (in use by another app)") },
        { "Hotkey_Record_Recording", new("キーを入力... (Esc で取消)", "Press keys... (Esc cancel)") },
        { "Hotkey_Reset", new("既定に戻す", "Reset") },
        { "Setting_Language", new("表示言語", "Display Language") },
        { "Setting_Language_Desc", new("アプリ全体のインターフェース言語を選択します", "Choose the interface language") },
        { "Lang_Auto", new("自動検出 (OS 設定)", "Auto Detect (System)") },
        { "Lang_Ja", new("日本語", "日本語 (Japanese)") },
        { "Lang_En", new("English", "English") },

        // Settings: Appearance & Flyout
        { "Section_Flyout", new("外観とフライアウト", "Appearance & Flyout") },
        { "Setting_Theme", new("アプリテーマ", "App Theme") },
        { "Setting_Theme_Desc", new("フライアウト・設定・メニューの外観モードを設定します", "Choose Light, Dark, or follow Windows system theme") },
        { "Theme_System", new("システム設定に従う", "Use system setting") },
        { "Theme_Light", new("ライト", "Light") },
        { "Theme_Dark", new("ダーク", "Dark") },
        { "Setting_UseSystemAccent", new("Windows のアクセントカラーを使用", "Use Windows Accent Color") },
        { "Setting_UseSystemAccent_Desc", new("OS の個人用設定で選んだアクセントカラーに自動追従します", "Automatically sync with your Windows accent color") },
        { "Setting_AccentColor", new("アクセントカラー", "Accent Color") },
        { "Setting_AccentColor_Desc", new("プライマリアクションや選択インジケーターの色を設定します", "Color used for primary actions and active indicators") },
        { "Color_Preset", new("プリセット", "Presets") },
        { "Color_Custom", new("カスタム RGB / HEX", "Custom RGB / HEX") },
        { "Color_Red", new("赤 (R)", "Red (R)") },
        { "Color_Green", new("緑 (G)", "Green (G)") },
        { "Color_Blue", new("青 (B)", "Blue (B)") },
        { "Color_Hex", new("HEX", "HEX") },
        { "Accent_Blue", new("Windows ブルー", "Windows Blue") },
        { "Accent_Teal", new("ティール", "Teal") },
        { "Accent_Green", new("エメラルド", "Emerald") },
        { "Accent_Purple", new("アイリス", "Iris") },
        { "Accent_Pink", new("ローズ", "Rose") },
        { "Accent_Orange", new("アンバー", "Amber") },
        { "Setting_Placement", new("表示位置", "Flyout Placement") },
        { "Setting_Placement_Desc", new("フライアウトが表示される画面上の基準位置を選びます", "Screen anchor where the flyout appears") },
        { "Placement_BottomRight", new("画面右下 (既定)", "Bottom Right (Default)") },
        { "Placement_TopRight", new("画面右上", "Top Right") },
        { "Placement_TopLeft", new("画面左上", "Top Left") },
        { "Placement_BottomLeft", new("画面左下", "Bottom Left") },
        { "Placement_NearCursor", new("マウスカーソル付近", "Near Mouse Cursor") },
        { "Setting_Opacity", new("背景の不透明度 (アクリル)", "Surface Opacity (Acrylic)") },
        { "Setting_Opacity_Desc", new("Windows 11 アクリル背景の透け具合を調整します", "Adjust frosted glass opacity of the flyout surface") },
        { "Setting_Duration", new("自動非表示までの時間", "Auto-Dismiss Delay") },
        { "Setting_Duration_Desc", new("マウスを乗せない場合に自動で閉じるまでの秒数", "Seconds before the flyout dismisses automatically") },
        { "Setting_HoverDuration", new("マウス離脱後の待機時間", "Hover Leave Delay") },
        { "Setting_HoverDuration_Desc", new("フライアウトからカーソルが離れた後に閉じるまでの秒数", "Seconds to wait after the cursor leaves the flyout") },
        { "Setting_SuppressFullscreen", new("全画面アプリ中は表示しない", "Suppress in Fullscreen Apps") },
        { "Setting_SuppressFullscreen_Desc", new("ゲーム・動画視聴・プレゼン中はフライアウトを自動抑制します", "Automatically suppress flyouts during fullscreen games, videos, or presentations") },

        // Settings: Taskbar Widget
        { "Section_Widget", new("タスクバーウィジェット", "Taskbar Widget") },
        { "Setting_ShowTaskbarWidget", new("タスクバーウィジェットを表示", "Show Taskbar Widget") },
        { "Setting_ShowTaskbarWidget_Desc", new("タスクバーに最新のコピー内容を常時表示し、1クリックでフライアウトを開きます", "Show a sleek pill on the taskbar for 1-click flyout access") },
        { "Setting_WidgetPosition", new("配置位置", "Widget Position") },
        { "Setting_WidgetPosition_Desc", new("タスクバー上の表示エリアを選択します", "Choose where the widget docks along the taskbar") },
        { "Setting_WidgetAutoAlign", new("トレイ (^) 左隣に自動吸着", "Auto-Dock Beside System Tray (^)") },
        { "Setting_WidgetAutoAlign_Desc", new("トレイ領域の幅変化に合わせて重ならないよう自動調整します", "Automatically shifts left of tray icons to avoid overlap") },
        { "Setting_WidgetMonitor", new("表示モニター", "Display Monitor") },
        { "Setting_WidgetMonitor_Desc", new("マルチモニター環境での表示先ディスプレイを選びます", "Choose which display hosts the widget") },
        { "Setting_WidgetOffsetX", new("水平オフセット微調整", "Horizontal Offset") },
        { "Setting_WidgetOffsetX_Desc", new("ウィジェットの左右位置をピクセル単位で調整します", "Fine-tune horizontal alignment in pixels") },
        { "Setting_WidgetTextColor", new("文字色モード", "Text Color") },
        { "Setting_WidgetTextColor_Desc", new("タスクバーのテーマに合わせて自動最適化するか、固定色を選びます", "Auto-match Windows taskbar theme or force light/dark text") },
        { "Widget_TextColor_Auto", new("自動 (タスクバー連動)", "Auto (Match Taskbar)") },
        { "Widget_TextColor_Light", new("常に白 (ダーク向け)", "Always White (Dark Taskbar)") },
        { "Widget_TextColor_Dark", new("常に黒 (ライト向け)", "Always Black (Light Taskbar)") },
        { "Widget_Empty", new("クリップボード空", "Clipboard empty") },
        { "Widget_ClickHint", new("クリックでフライアウトを開く", "Click to open flyout") },
        { "Widget_Hide", new("タスクバーウィジェットを非表示", "Hide Taskbar Widget") },
        { "Widget_Position_Header", new("配置位置", "Position") },
        { "Widget_Pos_TrayLeft", new("タスクバー右 (トレイ左隣)", "Taskbar Right (Near Tray)") },
        { "Widget_Pos_CenterRight", new("タスクバー中央・右寄り", "Taskbar Center-Right") },
        { "Widget_Pos_CenterLeft", new("タスクバー中央・左寄り", "Taskbar Center-Left") },
        { "Widget_Pos_FarLeft", new("タスクバー左端", "Taskbar Far-Left") },
        { "Widget_Pos_AboveTaskbar", new("画面右下 (タスクバー直上)", "Above Taskbar (Bottom-Right)") },
        { "Widget_Monitor_Header", new("表示モニター", "Monitor") },
        { "Widget_Monitor_Primary", new("メインモニター", "Primary Monitor") },
        { "Widget_Monitor_Cursor", new("カーソルのあるモニター", "Active Cursor Monitor") },
        { "Widget_Monitor_Display1", new("ディスプレイ 1", "Display 1") },
        { "Widget_Monitor_Display2", new("ディスプレイ 2", "Display 2") },
        { "Widget_Monitor_Display3", new("ディスプレイ 3", "Display 3") },

        // Settings: Detectors
        { "Section_Detectors", new("データ型フィルター", "Data Type Filters") },
        { "Section_Detectors_Core", new("基本データ型", "Core Data Types") },
        { "Section_Detectors_Tools", new("スマート変換・開発者ツール", "Smart Tools & Developer Helpers") },
        { "Detector_HexColor", new("カラーコード (HEX / RGB / HSL)", "Color Code (HEX / RGB / HSL)") },
        { "Detector_HexColor_Desc", new("#RRGGBB, rgb(), hsl() を検知し、色見本と各形式への変換を提案", "Detects HEX, RGB, and HSL colors with live swatch and conversions") },
        { "Detector_Json", new("JSON データ", "JSON Data") },
        { "Detector_Json_Desc", new("JSON の構文を解析し、整形・1行圧縮コピーを提案", "Parses JSON objects/arrays for pretty-print or minify") },
        { "Detector_Url", new("Web URL", "Web URL") },
        { "Detector_Url_Desc", new("HTTP/HTTPS リンクを検知し、ブラウザ起動・QR 生成・ドメイン抽出を提案", "Detects links with browser launch, QR code, and domain copy") },
        { "Detector_Email", new("メールアドレス", "Email Address") },
        { "Detector_Email_Desc", new("メール作成、ドメイン・ユーザー名コピーを提案", "Compose mail or copy domain/username") },
        { "Detector_FileDrop", new("エクスプローラーのファイル / フォルダ", "Explorer Files & Folders") },
        { "Detector_FileDrop_Desc", new("コピーしたファイル・フォルダのパス取得、引用符付きコピー、場所を開く機能を提供", "Copy full/quoted paths, file names, or reveal in Explorer") },
        { "Detector_Image", new("画像", "Image") },
        { "Detector_Image_Desc", new("クリップボード画像のプレビュー表示、PNG/JPG 保存、解像度情報コピーを提供", "Preview images, save to PNG/JPG/BMP, or copy dimensions") },
        { "Detector_Timestamp", new("Unix タイムスタンプ", "Unix Timestamp") },
        { "Detector_Timestamp_Desc", new("10桁(秒)・13桁(ミリ秒) の Epoch を人間が読める日時・ISO 形式へ変換", "Converts 10-digit (s) and 13-digit (ms) timestamps to local/ISO dates") },
        { "Detector_IsoDateTime", new("ISO 8601 日時", "ISO 8601 Date/Time") },
        { "Detector_IsoDateTime_Desc", new("ISO 8601 日時文字列をローカル日時や Unix タイムスタンプへ変換", "Converts ISO 8601 timestamps to local time and Unix epoch") },
        { "Detector_Base64", new("Base64 データ", "Base64 Data") },
        { "Detector_Base64_Desc", new("Base64 文字列や Data URI を復号し、テキストや画像として展開", "Decodes Base64 strings and Data URIs to text or images") },
        { "Detector_Table", new("表データ (CSV / TSV)", "Table Data (CSV / TSV)") },
        { "Detector_Table_Desc", new("表データを Markdown テーブルや JSON 配列へ変換", "Converts CSV/TSV tables to Markdown or JSON arrays") },
        { "Detector_Code", new("コードスニペット", "Code Snippet") },
        { "Detector_Code_Desc", new("ソースコードを検知し、インデント正規化や HTML エスケープを提供", "Detects code blocks for indent normalization and HTML escaping") },
        { "Detector_PlainText", new("プレーンテキスト", "Plain Text") },
        { "Detector_PlainText_Desc", new("空白トリム、文字数カウント、大文字/小文字変換を提供", "Whitespace trim, text statistics, and case transforms") },
        { "Detector_CleanUrl", new("クリーン URL (トラッカー除去)", "Clean URL (Strip Trackers)") },
        { "Detector_CleanUrl_Desc", new("UTM や広告識別パラメータを除去し、パーセントエンコードを保ったままコピー", "Strips tracking query params while preserving percent-encoding") },
        { "Detector_CaseConverter", new("識別子ケース変換", "Identifier Case Converter") },
        { "Detector_CaseConverter_Desc", new("camelCase / snake_case / kebab-case / PascalCase / CONSTANT_CASE を相互変換", "Converts multi-word identifiers across naming conventions") },
        { "Detector_Jwt", new("JWT トークン", "JWT Token") },
        { "Detector_Jwt_Desc", new("JWT をデコードし、有効期限 (exp) とクレーム JSON を表示", "Decodes JWT header/payload and shows expiration status") },
        { "Detector_Ocr", new("画像文字起こし (オンデバイス OCR)", "Image Text Extraction (OCR)") },
        { "Detector_Ocr_Desc", new("画像コピー時に Windows OCR で文字を抽出してコピー・貼り付け", "Extract readable text from copied images locally on device") },
        { "Detector_FilePath", new("パス文字列変換 (Windows / WSL)", "File Paths (Windows / WSL)") },
        { "Detector_FilePath_Desc", new("パス文字列を検知し、/ 変換、WSL パス変換、ターミナル起動を提案", "Convert slashes, map Windows/WSL paths, and open in terminal") },
        { "Detector_Math", new("クイック電卓 & 進数変換", "Quick Math & Hex/Binary") },
        { "Detector_Math_Desc", new("数式や 0x/0b 表記を即計算し、10進・16進・2進へ変換", "Evaluate arithmetic expressions and convert 0x/0b numbers safely") },
        { "Detector_Uuid", new("UUID / GUID", "UUID / GUID") },
        { "Detector_Uuid_Desc", new("UUID の大文字/小文字/ハイフン除去と v4 / v7 新規生成を提供", "Format UUIDs and generate fresh v4 or time-ordered v7 IDs") },

        // Settings: History & Privacy
        { "Section_History", new("履歴とプライバシー", "History & Privacy") },
        { "Setting_IgnorePasswordManagers", new("パスワード管理ツールのコピーを除外", "Ignore Password Managers") },
        { "Setting_IgnorePasswordManagers_Desc", new("1Password、Bitwarden、KeePass 等および機密クリップボード形式を自動除外します", "Automatically excludes password managers and sensitive clipboard flags") },
        { "Setting_ExcludedApps", new("除外するアプリ (プロセス名)", "Excluded Apps (Process Names)") },
        { "Setting_ExcludedApps_Desc", new("カンマ区切りで入力したアプリのコピーを自動無視します（例: mstsc, Code）", "Comma-separated process names to ignore (e.g. mstsc, Code)") },
        { "Setting_PersistHistory", new("テキスト履歴をローカルに保存", "Persist Text History Locally") },
        { "Setting_PersistHistory_Desc", new("再起動後もテキスト履歴とピン留め項目を復元します（画像は保存しません）", "Restore text & pinned history across restarts (images are never saved to disk)") },
        { "Setting_ClearHistory", new("クリップボード履歴をクリア", "Clear Clipboard History") },
        { "Setting_ClearHistory_Desc", new("現在のメモリ上の履歴項目数: {0} 件", "Currently stored items: {0}") },
        { "Setting_ClearHistory_Btn", new("履歴をクリア", "Clear History") },
        { "Setting_ClearHistory_Done", new("履歴をクリアしました", "History cleared") },

        // Settings: Updates & About
        { "Section_Updates", new("更新プログラム", "Updates") },
        { "Setting_AutoUpdate", new("自動アップデート確認", "Automatic Update Checks") },
        { "Setting_AutoUpdate_Desc", new("GitHub Releases を確認し、SHA-256 検証済みインストーラーのみ安全に適用します", "Checks GitHub Releases and enforces strict SHA-256 verification") },
        { "Setting_ManualUpdate", new("最新バージョンの手動確認", "Manual Update Check") },
        { "Setting_ManualUpdate_Desc", new("GitHub Releases で新しいバージョンが公開されているか確認します", "Check GitHub Releases now for a newer version") },
        { "Update_CheckNow", new("更新を確認", "Check Now") },
        { "Update_Checking", new("更新を確認しています...", "Checking for updates...") },
        { "Update_UpToDate", new("最新バージョンです", "You're up to date") },
        { "Update_Available", new("v{0} が利用可能です", "Version {0} is available") },
        { "Update_InstallNow", new("今すぐ更新", "Update Now") },
        { "Update_Downloading", new("ダウンロード中 ({0}%)...", "Downloading ({0}%)...") },
        { "Update_Verifying", new("SHA-256 署名を検証して起動中...", "Verifying SHA-256 & starting installer...") },
        { "Update_Failed", new("更新の確認または検証に失敗しました", "Update check or verification failed") },
        { "Update_Available_Title", new("アップデートが利用可能です", "Update Available") },
        { "Update_Available_Body", new("ClipFlyout v{0} が利用可能です。SHA-256 検証付きで更新できます。", "ClipFlyout v{0} is available with SHA-256 verified install.") },
        { "Update_Error_Dialog", new("アップデートのインストールに失敗しました:\n{0}", "Failed to install update:\n{0}") },
        { "Update_Now", new("今すぐ更新", "Update Now") },
        { "Update_Now_Desc", new("検証済みインストーラーをダウンロードして適用", "Download verified installer and restart") },
        { "Update_Details", new("リリースノートを見る", "Release Notes") },
        { "Update_Details_Desc", new("ブラウザで GitHub リリースページを開きます", "Open release page in browser") },

        { "Section_About", new("アプリについて", "About") },
        { "About_Privacy_Title", new("完全ローカル解析", "100% Local Processing") },
        { "About_Privacy_Desc", new("クリップボードの内容が外部サーバーへ送信されることはありません。", "Clipboard contents are analyzed strictly on this PC and never sent externally.") },
        { "About_Version", new("バージョン: {0}", "Version: {0}") },
        { "About_Runtime", new("ランタイム: .NET 10 / Windows 11 Fluent", "Runtime: .NET 10 / Windows 11 Fluent") },
        { "About_Github", new("GitHub リポジトリ", "GitHub Repository") },
        { "About_Github_Desc", new("ソースコード・Issue・リリースを確認します", "View source code, issues, and releases") },
        { "About_Open", new("開く", "Open") },
        { "About_Reset", new("設定を初期値に戻す", "Reset to Defaults") },
        { "About_Reset_Desc", new("すべての設定を初期状態にリセットします", "Restore all preferences to factory defaults") },
        { "About_Reset_Confirm", new("すべての設定を初期値に戻しますか？", "Reset all settings to their default values?") },
        { "Dialog_Confirm", new("実行する", "Confirm") },
        { "Dialog_Cancel", new("キャンセル", "Cancel") },
        { "Dialog_OK", new("OK", "OK") },
        { "Credits_Author", new("開発・著作: featlis", "Developed by featlis") },
        { "Credits_License", new("ライセンス: GNU General Public License version 3 (GPL-3.0-or-later)", "License: GNU General Public License version 3 (GPL-3.0-or-later)") },

        // Welcome Setup Wizard
        { "Welcome_Title", new("ClipFlyout へようこそ", "Welcome to ClipFlyout") },
        { "Welcome_Subtitle", new("コピーするだけで、整形・変換・保存のアクションを即座に提案します", "Instantly suggest actions to format, convert, and save whatever you copy") },
        { "Welcome_Feat1_Title", new("スマートなローカル解析", "Smart Local Detection") },
        { "Welcome_Feat1_Desc", new("カラー、URL、JSON、JWT、ファイル、画像、日時をローカルで瞬時に解析して最適な操作を提案", "Detects colors, URLs, JSON, JWT, files, images, and dates locally with 1-click actions") },
        { "Welcome_Feat2_Title", new("タスクバーウィジェット & キーボード操作", "Taskbar Widget & Keyboard First") },
        { "Welcome_Feat2_Desc", new("タスクバーに最新のコピーを常駐表示。Alt+Shift+C や Esc / 1-9 キーで素早く操作", "Dock a sleek pill on your taskbar and drive actions rapidly with Alt+Shift+C, Esc, and 1–9") },
        { "Welcome_Feat3_Title", new("プライバシー重視・完全ローカル", "Privacy First & Local") },
        { "Welcome_Feat3_Desc", new("外部送信ゼロ。1Password や Bitwarden などのパスワード管理ツールは自動で除外", "Zero cloud telemetry. Password managers and sensitive clipboard formats are automatically ignored") },
        { "Welcome_Customize", new("詳細をカスタマイズ", "Customize Settings") },
        { "Welcome_UseDefaults", new("この設定で始める (推奨)", "Get Started (Recommended)") },
        { "Welcome_Language", new("表示言語", "Display Language") },
        { "Welcome_Language_Desc", new("インターフェースの表示言語を選択します", "Select the display language for ClipFlyout") }
    };
}
