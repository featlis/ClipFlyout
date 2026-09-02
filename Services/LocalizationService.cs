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
            _currentLanguage = value;
            UpdateLanguageResolution();
            LanguageChanged?.Invoke();
        }
    }

    public bool IsJapanese => _isJapanese;

    public LocalizationService()
    {
        UpdateLanguageResolution();
    }

    private void UpdateLanguageResolution()
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
        { "Action_CopyQrCode", new("QRコード画像コピー", "Copy QR Image") },
        { "Action_CopyQrCode_Desc", new("QRコードを生成してクリップボードにコピー", "Generate QR code and copy image") },
        { "Action_CopyDomain", new("ホスト名をコピー", "Copy Domain") },
        { "Action_CopyDomain_Desc", new("ドメイン/ホスト名部分のみをコピー", "Copy host domain name only") },

        // Actions: Code
        { "Action_AdjustIndent", new("インデント調整", "Adjust Indent") },
        { "Action_AdjustIndent_Desc", new("インデントを半角スペース2文字に正規化", "Normalize indentation to 2 spaces") },
        { "Action_EscapeHtml", new("HTML特殊文字エスケープ", "Escape HTML") },
        { "Action_EscapeHtml_Desc", new("<, >, &, \" をHTMLエンティティに変換", "Convert <, >, &, \" into HTML entities") },

        // Actions: Image
        { "Action_SavePng", new("PNG保存", "Save PNG") },
        { "Action_SavePng_Desc", new("クリップボードの画像をファイルに保存", "Save clipboard image to PNG file") },
        { "Action_CopyImageInfo", new("画像情報コピー", "Copy Image Info") },
        { "Action_CopyImageInfo_Desc", new("解像度・アスペクト比をテキストでコピー", "Copy resolution and aspect ratio stats") },

        // Actions: Plain Text
        { "Action_TrimWhitespace", new("空白トリム", "Trim Whitespace") },
        { "Action_TrimWhitespace_Desc", new("前後の余分な空白・空行を削除してコピー", "Remove leading/trailing whitespace & empty lines") },
        { "Action_TextStats", new("統計情報コピー", "Copy Text Stats") },
        { "Action_TextStats_Desc", new("文字数・単語数・行数情報をコピー", "Copy character, word, and line count") },
        { "Action_UpperCase", new("大文字化", "UPPERCASE") },
        { "Action_UpperCase_Desc", new("すべての英字を大文字に変換してコピー", "Convert all letters to uppercase") },
        { "Action_LowerCase", new("小文字化", "lowercase") },
        { "Action_LowerCase_Desc", new("すべての英字を小文字に変換してコピー", "Convert all letters to lowercase") },

        // Toasts & Notifications
        { "Toast_Copied", new("クリップボードにコピーしました", "Copied to clipboard") },
        { "Toast_FormattedJsonCopied", new("整形済みJSONをコピーしました", "Prettified JSON copied") },
        { "Toast_MinifiedJsonCopied", new("1行化JSONをコピーしました", "Minified JSON copied") },
        { "Toast_QrCopied", new("QRコード画像をコピーしました", "QR Code image copied") },
        { "Toast_ImageSaved", new("画像を保存しました: {0}", "Image saved: {0}") },
        { "Toast_BrowserOpened", new("ブラウザを開きました", "Opened in browser") },

        // Tray Menu & Status
        { "Tray_TitleActive", new("ClipFlyout - 監視中", "ClipFlyout - Monitoring Active") },
        { "Tray_TitlePaused", new("ClipFlyout - 一時停止中", "ClipFlyout - Monitoring Paused") },
        { "Tray_ToggleMonitoring", new("クリップボード監視を切り替え", "Toggle Monitoring") },
        { "Tray_LangJapanese", new("言語: 日本語 (Japanese)", "Language: 日本語 (Japanese)") },
        { "Tray_LangEnglish", new("Language: English", "Language: English") },
        { "Tray_LangAuto", new("言語: 自動検出 (Auto)", "Language: Auto Detect") },
        { "Tray_Exit", new("終了 (Exit)", "Exit") }
    };
}
