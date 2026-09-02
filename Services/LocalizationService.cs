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
        { "Type_Code", new("コードスニペット", "Code Snippet") },
        { "Type_Image", new("画像", "Image") },
        { "Type_PlainText", new("プレーンテキスト", "Plain Text") },

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

        // Toasts & Notifications
        { "Toast_Copied", new("コピーしました", "Copied to clipboard") },
        { "Toast_FormattedJsonCopied", new("整形済みJSONをコピーしました", "Prettified JSON copied") },
        { "Toast_MinifiedJsonCopied", new("1行化JSONをコピーしました", "Minified JSON copied") },
        { "Toast_QrCopied", new("QRコード画像をコピーしました", "QR Code image copied") },
        { "Toast_ImageSaved", new("画像を保存しました: {0}", "Image saved: {0}") },
        { "Toast_BrowserOpened", new("ブラウザを開きました", "Opened in browser") },

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
        { "Lang_Auto", new("自動検出 (OS設定)", "Auto Detect (System)") },
        { "Lang_Ja", new("日本語 (Japanese)", "Japanese") },
        { "Lang_En", new("English (英語)", "English") },

        { "Section_Flyout", new("フライアウト動作", "Flyout Behavior") },
        { "Setting_Placement", new("表示位置", "Flyout Placement") },
        { "Setting_Placement_Desc", new("フライアウトが出現する画面上の位置を指定します", "Screen position where flyout appears") },
        { "Placement_BottomRight", new("画面右下 (標準)", "Bottom Right (Default)") },
        { "Placement_TopRight", new("画面右上", "Top Right") },
        { "Placement_BottomLeft", new("画面左下", "Bottom Left") },
        { "Placement_NearCursor", new("マウスカーソル付近", "Near Mouse Cursor") },
        { "Setting_Duration", new("自動非表示タイマー", "Auto-Dismiss Timeout") },
        { "Setting_Duration_Desc", new("操作がない場合にフライアウトが消えるまでの時間", "Time before flyout automatically fades out") },
        { "Setting_HoverDuration", new("マウス離脱後の消滅時間", "Mouse Leave Dismiss Timeout") },
        { "Setting_HoverDuration_Desc", new("ホバー解除から消えるまでのカウントダウン時間", "Countdown after cursor leaves flyout") },

        { "Section_Detectors", new("データ型検出フィルター", "Data Type Detection Filters") },
        { "Section_Detectors_Desc", new("検知してフライアウトを表示するデータ型を選択できます", "Select which clipboard data types to detect and act upon") },
        { "Detector_HexColor", new("HEXカラーコード", "HEX Color Code") },
        { "Detector_HexColor_Desc", new("#RGB, #RRGGBB などを検知し、色見本プレビューとRGB/HSL変換を提供", "Detects #RGB, #RRGGBB colors, provides swatch and RGB/HSL conversion") },
        { "Detector_Json", new("JSON テキスト", "JSON Text") },
        { "Detector_Json_Desc", new("JSON オブジェクト・配列を検証し、整形または1行化コピーを提供", "Validates JSON structure, provides format and minify actions") },
        { "Detector_Url", new("Web URL", "Web URL") },
        { "Detector_Url_Desc", new("Webリンクを検知し、ブラウザ起動やQRコード画像生成を提供", "Detects web URLs, provides browser open and QR code generator") },
        { "Detector_Code", new("コードスニペット", "Code Snippet") },
        { "Detector_Code_Desc", new("プログラミング構文を検知し、インデント調整やHTML特殊文字エスケープを提供", "Detects programming syntax, provides indent fix and HTML escape") },
        { "Detector_Image", new("画像", "Image") },
        { "Detector_Image_Desc", new("クリップボードの画像を検知し、PNG保存や解像度情報の確認を提供", "Detects clipboard bitmap, provides PNG export and image stats") },
        { "Detector_PlainText", new("プレーンテキスト", "Plain Text") },
        { "Detector_PlainText_Desc", new("通常の文字列の前後の余分な空白トリムや文字数・単語数統計を提供", "Provides whitespace trim and character/word statistics") },

        { "Section_About", new("バージョン情報 & プライバシー", "About & Privacy") },
        { "About_Privacy_Title", new("完全オフライン・プライバシー保護", "100% Offline & Private") },
        { "About_Privacy_Desc", new("ClipFlyout はすべての処理をお使いのPC内部で完結します。外部サーバーへの通信やテレメトリ送信は一切行いません。", "ClipFlyout processes everything locally in memory. Zero network communication or telemetry.") },
        { "About_Version", new("バージョン: v0.1.0", "Version: v0.1.0") },
        { "About_Github", new("GitHub リポジトリ", "GitHub Repository") },
        { "About_Reset", new("設定を初期値に戻す", "Reset to Defaults") },
        { "About_Reset_Confirm", new("すべての設定を初期値に戻しますか？", "Reset all settings to defaults?") }
    };
}
