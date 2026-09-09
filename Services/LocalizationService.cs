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
        { "Tray_TitleActive", new("ClipFlyout - 連携中", "ClipFlyout - Active") },
        { "Tray_TitlePaused", new("ClipFlyout - 一時停止中", "ClipFlyout - Paused") },
        { "Tray_ToggleMonitoring", new("クリップボード連携を一時停止/再開", "Toggle Clipboard Integration") },
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
        { "Setting_Monitoring", new("クリップボード連携", "Clipboard Integration") },
        { "Setting_Monitoring_Desc", new("クリップボードの変更を安全にローカル検知し、便利な操作を提案します", "Detect clipboard changes locally and suggest contextual actions") },
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
        { "Detector_CleanUrl", new("クリーンURL (トラッカー除去)", "Clean URL (Strip Trackers)") },
        { "Detector_CleanUrl_Desc", new("UTMパラメータや広告トラッカーを除去したURLコピーを提案", "Provides actions to copy URLs with UTM and tracking parameters stripped") },
        { "Detector_CaseConverter", new("識別子ケース変換", "Identifier Case Converter") },
        { "Detector_CaseConverter_Desc", new("camelCase, snake_case などの相互変換アクションを提案", "Provides case conversions between camelCase, snake_case, etc.") },
        { "Detector_Jwt", new("JWT トークン", "JWT Token") },
        { "Detector_Jwt_Desc", new("JWTを検知してクレームペイロードJSONを展開", "Detects JWT tokens and decodes payload claims") },

        { "Action_Paste", new("貼り付け", "Paste") },
        { "Action_Paste_Desc", new("アクティブなアプリへ貼り付け (Ctrl+V)", "Paste to active application (Ctrl+V)") },
        { "Action_Copy", new("コピー", "Copy") },
        { "Action_Copy_Desc", new("クリップボードへコピー", "Copy to clipboard") },
        { "Action_CopyCleanUrl", new("クリーンURLをコピー", "Copy Clean URL") },
        { "Action_CopyCleanUrl_Desc", new("トラッキングパラメータを除去してコピー", "Copy URL with tracking parameters stripped") },
        { "Action_DecodeUrl", new("URLデコード", "Decode URL") },
        { "Action_DecodeUrl_Desc", new("URLエンコード文字をデコードしてコピー", "Decode percent-encoded characters and copy") },
        { "Action_ToCamelCase_Desc", new("camelCase に変換してコピー", "Convert to camelCase and copy") },
        { "Action_ToSnakeCase_Desc", new("snake_case に変換してコピー", "Convert to snake_case and copy") },
        { "Action_ToKebabCase_Desc", new("kebab-case に変換してコピー", "Convert to kebab-case and copy") },
        { "Action_ToPascalCase_Desc", new("PascalCase に変換してコピー", "Convert to PascalCase and copy") },
        { "Action_ToConstantCase_Desc", new("CONSTANT_CASE に変換してコピー", "Convert to CONSTANT_CASE and copy") },
        { "Action_CopyJwtPayload", new("JWTペイロードをコピー", "Copy JWT Payload") },
        { "Action_CopyJwtPayload_Desc", new("JWTクレームJSONを整形してコピー", "Format and copy JWT claims JSON") },
        { "Action_CopyJwtHeader", new("JWTヘッダーをコピー", "Copy JWT Header") },
        { "Action_CopyJwtHeader_Desc", new("JWTヘッダーJSONをコピー", "Copy JWT header JSON") },

        { "Transform_Title", new("変換完了", "Transformed") },
        { "Badge_Transformed", new("変換済", "Transformed") },
        { "Toast_Pasted", new("貼り付けました", "Pasted to active app") },
        { "Toast_CleanUrlCopied", new("クリーンURLをコピーしました", "Clean URL copied") },
        { "Toast_UrlDecoded", new("URLをデコードしてコピーしました", "URL decoded and copied") },

        { "Setting_ShowTaskbarWidget", new("タスクバーウィジェットを表示", "Show taskbar widget") },
        { "Setting_ShowTaskbarWidget_Desc", new("タスクバー近傍に最新のコピー内容を小さく表示します", "Shows a compact pill with the latest copied item near the taskbar") },
        { "Setting_WidgetPosition", new("ウィジェットの表示位置", "Widget Position") },
        { "Setting_WidgetPosition_Desc", new("タスクバーに沿ったウィジェットの配置場所を選びます", "Select placement of the widget along the taskbar") },
        { "Setting_WidgetOffsetX", new("ウィジェット左右微調整", "Widget Horizontal Offset") },
        { "Setting_WidgetOffsetX_Desc", new("ウィジェットの左右位置をピクセル単位で微調整します", "Fine-tune widget position horizontally in pixels") },
        { "Setting_IgnorePasswordManagers", new("パスワード管理ツールのコピーを除外", "Ignore password managers") },
        { "Setting_IgnorePasswordManagers_Desc", new("1Password、Bitwarden、KeePass 等からのコピーを検知・保存しません", "Suppresses flyouts and history for copies from 1Password, Bitwarden, KeePass, etc.") },
        { "Setting_EnableRecallHotkey", new("リコールショートカット (Alt+Shift+C)", "Recall hotkey (Alt+Shift+C)") },
        { "Setting_EnableRecallHotkey_Desc", new("非表示になった直前のフライアウトをショートカットで再表示します", "Press Alt+Shift+C to re-open the last flyout") },

        { "Widget_Empty", new("(クリップボード空)", "(Clipboard empty)") },
        { "Widget_Hide", new("タスクバーウィジェットを非表示", "Hide taskbar widget") },
        { "Widget_Position_Header", new("表示位置", "Position") },
        { "Widget_Pos_TrayLeft", new("タスクバー右 (トレイ左隣) [既定]", "Taskbar Right (Near Tray) [Default]") },
        { "Widget_Pos_CenterRight", new("タスクバー中央・右寄り", "Taskbar Center-Right") },
        { "Widget_Pos_CenterLeft", new("タスクバー中央・左寄り", "Taskbar Center-Left") },
        { "Widget_Pos_FarLeft", new("タスクバー左端", "Taskbar Far-Left") },
        { "Widget_Pos_AboveTaskbar", new("画面右下 (タスクバー直上)", "Above Taskbar (Bottom-Right)") },

        { "Tray_History", new("履歴", "History") },
        { "History_Empty", new("履歴はありません", "No history") },

        // Welcome Setup Wizard
        { "Welcome_Title", new("ClipFlyout へようこそ", "Welcome to ClipFlyout") },
        { "Welcome_Subtitle", new("コピーするだけで、整形・変換・保存のアクションを即座に提案します", "Instantly suggest actions to format, convert, and save whatever you copy") },
        { "Welcome_Feat1_Title", new("クリップボード連携", "Clipboard Integration") },
        { "Welcome_Feat1_Desc", new("テキスト、URL、JSON、カラーコード、画像を安全にローカル解析し、操作ボタンを表示", "Analyzes copied content locally and provides handy 1-click action buttons") },
        { "Welcome_Feat2_Title", new("タスクバーウィジェット", "Taskbar Widget") },
        { "Welcome_Feat2_Desc", new("タスクバーに最新のコピー内容を小さく表示。クリックでいつでもフライアウトを呼び出し", "Shows currently copied snippet docked seamlessly near the taskbar") },
        { "Welcome_Feat3_Title", new("プライバシー重視・完全ローカル", "Privacy First & Local") },
        { "Welcome_Feat3_Desc", new("外部通信なし。1PasswordやBitwardenなどのパスワード管理ツールは自動除外", "Zero external network requests. Password managers automatically excluded") },
        { "Welcome_Customize", new("詳細をカスタマイズ", "Customize Settings") },
        { "Welcome_UseDefaults", new("既定値で使用する (推奨)", "Use Defaults (Recommended)") },

        { "Section_About", new("アプリについて", "About") },
        { "About_Privacy_Title", new("ローカル処理について", "Local processing") },
        { "About_Privacy_Desc", new("クリップボードの解析はこのPC上で行われます。", "Clipboard analysis is performed on this PC.") },
        { "About_Version", new("バージョン: {0}", "Version: {0}") },
        { "About_Github", new("GitHub リポジトリ", "GitHub Repository") },
        { "About_Reset", new("設定を初期値に戻す", "Reset to Defaults") },
        { "About_Reset_Confirm", new("すべての設定を初期値に戻しますか？", "Reset all settings to defaults?") },
        { "Update_CheckNow", new("更新プログラムを確認", "Check for updates") },
        { "Update_Checking", new("更新プログラムを確認しています...", "Checking for updates...") },
        { "Update_UpToDate", new("最新バージョンです。", "You're up to date.") },
        { "Update_Available", new("v{0} を利用できます。今すぐ更新しますか？", "Version {0} is available. Update now?") },
        { "Update_InstallNow", new("今すぐ更新", "Update Now") },
        { "Update_Downloading", new("ダウンロードしてインストーラーを起動しています...", "Downloading update...") },
        { "Update_Failed", new("更新プログラムを確認またはダウンロードできませんでした。", "Couldn't check for or download an update.") },
        { "Update_Available_Title", new("新しいバージョンが利用可能です", "New version available") },
        { "Update_Available_Desc", new("ClipFlyout の更新プログラムがダウンロード可能です。", "A new version of ClipFlyout is available for download.") },
        { "Update_Now", new("今すぐ更新", "Update Now") },
        { "Update_Now_Desc", new("更新プログラムを適用して再起動します", "Install update and restart") },
        { "Update_Details", new("詳細", "Details") },
        { "Update_Details_Desc", new("ブラウザでリリースノートを開きます", "Open release notes in browser") },

        // Tabs
        { "Tab_General", new("全般", "General") },
        { "Tab_Appearance", new("外観 & フライアウト", "Appearance & Flyout") },
        { "Tab_Detectors", new("データ型フィルター", "Detectors") },
        { "Tab_Updates", new("更新プログラム", "Updates") },
        { "Tab_Credits", new("情報 & クレジット", "About & Credits") },

        // Color Picker
        { "Color_Preset", new("プリセットカラー", "Preset Colors") },
        { "Color_Custom", new("カスタムカラー (RGB)", "Custom Color (RGB)") },
        { "Color_Red", new("赤 (R)", "Red (R)") },
        { "Color_Green", new("緑 (G)", "Green (G)") },
        { "Color_Blue", new("青 (B)", "Blue (B)") },
        { "Color_Hex", new("カラーコード", "Color Code") },

        // Credits & License
        { "Section_Credits", new("開発・クレジット", "Developer & Credits") },
        { "Credits_Author", new("開発・著作: featlis", "Created & Developed by featlis") },
        { "Credits_Project", new("ClipFlyout プロジェクト", "ClipFlyout Project") },
        { "Credits_License", new("ライセンス: GNU General Public License version 3 (GPL-3.0-or-later)", "License: GNU General Public License version 3 (GPL-3.0-or-later)") },
        { "Credits_Desc", new("本ソフトウェアは自由ソフトウェアです。フリーソフトウェア財団が公表した GNU General Public License version 3 の定めに従い、誰でも無償で自由に使用、研究、改変、再配布することができます。", "This software is free software licensed under the GNU General Public License version 3 (GPL-3.0-or-later). You may freely use, study, modify, and redistribute it under the terms of the license.") }
    };
}
