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
            // Auto detection based on CurrentUICulture
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            _isJapanese = lang == "ja";
        }
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
        { "Type_HexColor", new("HEX カラー", "HEX Color") },
        { "Type_Json", new("JSON データ", "JSON Data") },
        { "Type_Url", new("Web URL", "Web URL") },
        { "Type_Email", new("メールアドレス", "Email address") },
        { "Type_Code", new("コードスニペット", "Code Snippet") },
        { "Type_Image", new("画像", "Image") },
        { "Type_PlainText", new("プレーンテキスト", "Plain Text") },
        { "Type_Timestamp", new("Unix タイムスタンプ", "Unix Timestamp") },
        { "Type_Base64", new("Base64 データ", "Base64 Data") },
        { "Type_TableData", new("表データ (CSV/TSV)", "Table Data (CSV/TSV)") },

        // Actions: Hex Color
        { "Action_CopyRgb", new("RGBコピー", "Copy RGB") },
        { "Action_CopyRgb_Desc", new("RGB形式 (rgb(r, g, b)) でコピー", "Copy in rgb(r, g, b) format") },
        { "Action_CopyHsl", new("HSLコピー", "Copy HSL") },
        { "Action_CopyHsl_Desc", new("HSL形式 (hsl(h, s%, l%)) でコピー", "Copy in hsl(h, s%, l%) format") },
        { "Action_CopyRgba", new("RGBAコピー", "Copy RGBA") },
        { "Action_CopyRgba_Desc", new("RGBA形式 (rgba(r, g, b, a)) でコピー", "Copy in rgba(r, g, b, a) format") },

        // Actions: JSON
        { "Action_FormatJson", new("整形してコピー", "Format & Copy") },
        { "Action_FormatJson_Desc", new("インデント付きで見やすく整形してコピー", "Prettify JSON with 2-space indentation") },
        { "Action_MinifyJson", new("1行化してコピー", "Minify & Copy") },
        { "Action_MinifyJson_Desc", new("余分な空白を除去して1行に圧縮", "Remove whitespace and minify to single line") },

        // Actions: URL
        { "Action_OpenBrowser", new("ブラウザで開く", "Open in Browser") },
        { "Action_OpenBrowser_Desc", new("既定のブラウザでリンク先を開く", "Open target link in default web browser") },
        { "Action_CopyQrCode", new("QR画像コピー", "Copy QR Image") },
        { "Action_CopyQrCode_Desc", new("QRコード画像をクリップボードにコピー", "Generate QR code and copy image") },
        { "Action_CopyDomain", new("ドメインコピー", "Copy Domain") },
        { "Action_CopyDomain_Desc", new("ドメイン/ホスト名部分のみをコピー", "Copy host domain name only") },
        { "Action_OpenEmail", new("メールを作成", "Compose Email") },
        { "Action_OpenEmail_Desc", new("既定のメールアプリで新規メッセージを作成", "Open a new message in the default email app") },
        { "Action_CopyEmailDomain", new("ドメインをコピー", "Copy Domain") },
        { "Action_CopyEmailDomain_Desc", new("メールアドレスのドメイン部分をコピー", "Copy the email domain") },
        { "Action_CopyEmailUser", new("ユーザー名をコピー", "Copy User") },
        { "Action_CopyEmailUser_Desc", new("@ より前の部分をコピー", "Copy the part before @") },

        // Actions: Code
        { "Action_AdjustIndent", new("インデント調整", "Adjust Indent") },
        { "Action_AdjustIndent_Desc", new("インデントを半角スペース2文字に正規化", "Normalize indentation to 2 spaces") },
        { "Action_EscapeHtml", new("HTMLエスケープ", "Escape HTML") },
        { "Action_EscapeHtml_Desc", new("<, >, &, \" をHTMLエンティティに変換", "Convert <, >, &, \" into HTML entities") },

        // Actions: Image
        { "Action_SavePng", new("PNG保存", "Save PNG") },
        { "Action_SavePng_Desc", new("クリップボードの画像をファイルに保存", "Save clipboard image to PNG file") },
        { "Action_CopyImageInfo", new("画像情報コピー", "Copy Info") },
        { "Action_CopyImageInfo_Desc", new("解像度・アスペクト比をテキストでコピー", "Copy resolution and aspect ratio stats") },

        // Actions: Plain Text
        { "Action_TrimWhitespace", new("空白トリム", "Trim Whitespace") },
        { "Action_TrimWhitespace_Desc", new("前後の余分な空白・空行を削除してコピー", "Remove leading/trailing whitespace") },
        { "Action_TextStats", new("文字数カウント", "Count Characters") },
        { "Action_TextStats_Desc", new("文字数・単語数・行数情報をコピー", "Copy character, word, and line count") },
        { "Action_UpperCase", new("大文字化", "UPPERCASE") },
        { "Action_UpperCase_Desc", new("すべての英字を大文字に変換してコピー", "Convert all letters to uppercase") },
        { "Action_LowerCase", new("小文字化", "lowercase") },
        { "Action_LowerCase_Desc", new("すべての英字を小文字に変換してコピー", "Convert all letters to lowercase") },

        // Actions: Unix Timestamp
        { "Action_CopyLocalDate", new("ローカル日時コピー", "Copy Local Time") },
        { "Action_CopyLocalDate_Desc", new("現在のタイムゾーンでフォーマットしてコピー", "Format and copy in local timezone") },
        { "Action_CopyIsoDate", new("ISO 8601コピー", "Copy ISO 8601") },
        { "Action_CopyIsoDate_Desc", new("UTC ISO 8601形式 (YYYY-MM-DDTHH:mm:ssZ) でコピー", "Copy in UTC ISO 8601 format") },
        { "Action_CopyCurrentTimestamp", new("現在Epochコピー", "Copy Current Epoch") },
        { "Action_CopyCurrentTimestamp_Desc", new("現在日時のUnix秒タイムスタンプをコピー", "Copy current unix epoch timestamp in seconds") },

        // Actions: Base64
        { "Action_DecodeBase64", new("デコードしてコピー", "Decode & Copy") },
        { "Action_DecodeBase64_Desc", new("Base64文字列をプレーンテキストに復号", "Decode Base64 string into plain text") },
        { "Action_CopyDecodedImage", new("画像としてコピー", "Copy as Image") },
        { "Action_CopyDecodedImage_Desc", new("Base64画像データを画像としてクリップボードへ展開", "Copy decoded image to clipboard") },

        // Actions: Table Data (CSV / TSV)
        { "Action_ToMarkdownTable", new("Markdown表に変換", "To Markdown Table") },
        { "Action_ToMarkdownTable_Desc", new("TSV/CSVを表形式のMarkdownテーブルに整形してコピー", "Format TSV/CSV into a Markdown table") },
        { "Action_ToJsonArray", new("JSON配列に変換", "To JSON Array") },
        { "Action_ToJsonArray_Desc", new("ヘッダー行をキーとしたJSONオブジェクトの配列に変換", "Convert table rows into an array of JSON objects") },

        // Toasts & Notifications
        { "Toast_Copied", new("コピーしました", "Copied to clipboard") },
        { "Toast_FormattedJsonCopied", new("整形済みJSONをコピーしました", "Prettified JSON copied") },
        { "Toast_MinifiedJsonCopied", new("1行化JSONをコピーしました", "Minified JSON copied") },
        { "Toast_QrCopied", new("QRコード画像をコピーしました", "QR Code image copied") },
        { "Toast_ImageSaved", new("画像を保存しました: {0}", "Image saved: {0}") },
        { "Toast_BrowserOpened", new("ブラウザを開きました", "Opened in browser") },
        { "Toast_EmailOpened", new("メールアプリを開きました", "Opened email app") },
        { "Toast_Base64Decoded", new("Base64をデコードしてコピーしました", "Base64 decoded & copied") },
        { "Toast_MarkdownTableCopied", new("Markdownテーブルをコピーしました", "Markdown table copied") },
        { "Toast_JsonArrayCopied", new("JSON配列をコピーしました", "JSON array copied") },

        // Tray Menu & Status
        { "Tray_Settings", new("設定...", "Settings...") },
        { "Tray_Theme", new("テーマ", "Theme") },
        { "Tray_ThemeSystem", new("システム連動", "System Default") },
        { "Tray_ThemeLight", new("ライト", "Light") },
        { "Tray_ThemeDark", new("ダーク", "Dark") },
        { "Tray_TitleActive", new("ClipFlyout - 監視中", "ClipFlyout - Monitoring Active") },
        { "Tray_TitlePaused", new("ClipFlyout - 一時停止中", "ClipFlyout - Monitoring Paused") },
        { "Tray_ToggleMonitoring", new("クリップボード監視を切り替え", "Toggle Monitoring") },
        { "Tray_Language", new("言語 (Language)", "Language") },
        { "Tray_LangJapanese", new("日本語 (Japanese)", "日本語 (Japanese)") },
        { "Tray_LangEnglish", new("English", "English") },
        { "Tray_LangAuto", new("自動検出 (Auto)", "Auto Detect") },
        { "Tray_Exit", new("終了", "Exit") },
        { "Tray_CheckUpdates", new("更新プログラムを確認", "Check for updates") },

        // Settings Window
        { "Settings_Title", new("ClipFlyout 設定", "ClipFlyout Settings") },
        { "Settings_SubTitle", new("クリップボード検出と動作のカスタマイズ", "Customize clipboard detection and behavior") },
        { "Section_General", new("全般", "General") },
        { "Setting_Monitoring", new("クリップボード監視", "Clipboard Monitoring") },
        { "Setting_Monitoring_Desc", new("クリップボードの変更を検知してアクションを提案します", "Detect clipboard changes and suggest contextual actions") },
        { "Setting_Startup", new("Windows 起動時に自動開始", "Run on Windows Startup") },
        { "Setting_Startup_Desc", new("PC起動時にバックグラウンドでClipFlyoutを開始します", "Automatically start ClipFlyout in background on boot") },
        { "Setting_Theme", new("アプリテーマ", "App Theme") },
        { "Setting_Theme_Desc", new("フライアウトおよび設定画面の外観スタイルを設定します", "Visual appearance of flyout and settings window") },
        { "Theme_System", new("システム設定に従う", "System Default") },
        { "Theme_Light", new("ライト (Light)", "Light") },
        { "Theme_Dark", new("ダーク (Dark)", "Dark") },
        { "Setting_Language", new("表示言語", "Display Language") },
        { "Setting_Language_Desc", new("インターフェースの表示言語を設定します", "Interface language for the application") },
        { "Setting_AutoUpdate", new("自動アップデート", "Automatic updates") },
        { "Setting_AutoUpdate_Desc", new("GitHub Releases から更新を確認し、安全にインストールします", "Check GitHub Releases and securely install updates") },
        { "Lang_Auto", new("自動検出 (OS設定)", "Auto Detect (System)") },
        { "Lang_Ja", new("日本語 (Japanese)", "Japanese") },
        { "Lang_En", new("English (英語)", "English") },

        { "Section_Flyout", new("フライアウト動作 & 外観", "Flyout Behavior & Appearance") },
        { "Setting_Placement", new("表示位置", "Flyout Placement") },
        { "Setting_Placement_Desc", new("フライアウトが出現する画面上の位置を指定します", "Screen position where flyout appears") },
        { "Placement_BottomRight", new("画面右下 (標準)", "Bottom Right (Default)") },
        { "Placement_TopRight", new("画面右上", "Top Right") },
        { "Placement_TopLeft", new("画面左上", "Top Left") },
        { "Placement_BottomLeft", new("画面左下", "Bottom Left") },
        { "Placement_NearCursor", new("マウスカーソル付近", "Near Mouse Cursor") },
        { "Setting_Duration", new("自動非表示タイマー", "Auto-Dismiss Timeout") },
        { "Setting_Duration_Desc", new("操作がない場合にフライアウトが消えるまでの時間", "Time before flyout automatically fades out") },
        { "Setting_HoverDuration", new("マウス離脱後の消滅時間", "Mouse Leave Dismiss Timeout") },
        { "Setting_HoverDuration_Desc", new("ホバー解除から消えるまでのカウントダウン時間", "Countdown after cursor leaves flyout") },
        { "Setting_Opacity", new("背景の透明度 (アクリル効果)", "Background Opacity (Acrylic)") },
        { "Setting_Opacity_Desc", new("フライアウトのすりガラス・半透明度を調整します", "Adjust translucency of the flyout frosted card") },
        { "Setting_AccentColor", new("アクセントカラー", "Accent color") },
        { "Setting_AccentColor_Desc", new("主要な操作ボタンと強調表示の色を選びます", "Choose the color for primary actions and highlights") },
        { "Accent_Blue", new("ブルー", "Blue") },
        { "Accent_Purple", new("パープル", "Purple") },
        { "Accent_Pink", new("ピンク", "Pink") },
        { "Accent_Green", new("グリーン", "Green") },
        { "Accent_Orange", new("オレンジ", "Orange") },

        { "Section_Detectors", new("データ型検出フィルター", "Data Type Detection Filters") },
        { "Section_Detectors_Desc", new("検知してフライアウトを表示するデータ型を選択できます", "Select which clipboard data types to detect and act upon") },
        { "Detector_HexColor", new("HEXカラーコード", "HEX Color Code") },
        { "Detector_HexColor_Desc", new("#RGB, #RRGGBB などを検知し、色見本プレビューとRGB/HSL変換を提供", "Detects #RGB, #RRGGBB colors, provides swatch and RGB/HSL conversion") },
        { "Detector_Json", new("JSON テキスト", "JSON Text") },
        { "Detector_Json_Desc", new("JSON オブジェクト・配列を検証し、整形または1行化コピーを提供", "Validates JSON structure, provides format and minify actions") },
        { "Detector_Url", new("Web URL", "Web URL") },
        { "Detector_Url_Desc", new("Webリンクを検知し、ブラウザ起動やQRコード画像生成を提供", "Detects web URLs, provides browser open and QR code generator") },
        { "Detector_Email", new("メールアドレス", "Email Address") },
        { "Detector_Email_Desc", new("メール作成、ドメイン・ユーザー名のコピーを提案します", "Suggests composing email and copying its domain or user name") },
        { "Detector_Code", new("コードスニペット", "Code Snippet") },
        { "Detector_Code_Desc", new("プログラミング構文を検知し、インデント調整やHTML特殊文字エスケープを提供", "Detects programming syntax, provides indent fix and HTML escape") },
        { "Detector_Image", new("画像", "Image") },
        { "Detector_Image_Desc", new("クリップボードの画像を検知し、PNG保存や解像度情報の確認を提供", "Detects clipboard bitmap, provides PNG export and image stats") },
        { "Detector_PlainText", new("プレーンテキスト", "Plain Text") },
        { "Detector_PlainText_Desc", new("通常の文字列の前後の余分な空白トリムや文字数・単語数統計を提供", "Provides whitespace trim and character/word statistics") },
        { "Detector_Timestamp", new("Unix タイムスタンプ", "Unix Timestamp") },
        { "Detector_Timestamp_Desc", new("10桁(秒)や13桁(ミリ秒)の数値を検出し、日時プレビューとISO変換を提供", "Detects 10-digit (s) and 13-digit (ms) timestamps with human date previews") },
        { "Detector_Base64", new("Base64 データ", "Base64 Data") },
        { "Detector_Base64_Desc", new("Base64文字列やData URIを検出し、テキスト復号や画像展開を提供", "Detects Base64 text and Data URIs, provides decode and image extraction") },
        { "Detector_Table", new("表データ (CSV / TSV)", "Table Data (CSV / TSV)") },
        { "Detector_Table_Desc", new("ExcelやTSV/CSVのコピーからMarkdown表やJSON配列への自動変換を提供", "Detects spreadsheets / CSV rows, converts to Markdown table or JSON array") },

        { "Section_About", new("アプリについて", "About") },
        { "About_Privacy_Title", new("ローカル処理について", "Local processing") },
        { "About_Privacy_Desc", new("クリップボードの解析はこのPC上で行われます。", "Clipboard analysis is performed on this PC.") },
        { "About_Version", new("バージョン: v0.5.1", "Version: v0.5.1") },
        { "About_Github", new("GitHub リポジトリ", "GitHub Repository") },
        { "About_Reset", new("設定を初期値に戻す", "Reset to Defaults") },
        { "About_Reset_Confirm", new("すべての設定を初期値に戻しますか？", "Reset all settings to defaults?") },
        { "Update_CheckNow", new("更新プログラムを確認", "Check for updates") },
        { "Update_UpToDate", new("最新バージョンです。", "You're up to date.") },
        { "Update_Available", new("v{0} を利用できます。今すぐ更新しますか？", "Version {0} is available. Update now?") },
        { "Update_Failed", new("更新プログラムを確認またはダウンロードできませんでした。", "Couldn't check for or download an update.") }
    };
}
