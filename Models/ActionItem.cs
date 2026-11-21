using System;
using System.Windows;

namespace ClipFlyout.Models;

/// <summary>
/// Executable contextual action available for the detected clipboard payload.
/// </summary>
public record ActionItem(
    string LabelKey,
    string Label,
    string IconKey,
    string Description,
    Action ExecuteAction,
    bool IsPrimary = false,
    string? Subtitle = null,
    string? IconGlyph = null,
    string? ShortcutHint = null
)
{
    /// <summary>
    /// Display title for the action button (aliases Label for UI bindings).
    /// </summary>
    public string Title => Label;

    /// <summary>
    /// Gets display subtitle for rich card presentation (custom subtitle, or description fallback).
    /// </summary>
    public string DisplaySubtitle => !string.IsNullOrWhiteSpace(Subtitle) ? Subtitle : (Description ?? string.Empty);

    /// <summary>
    /// Returns whether this action has a non-empty explicit subtitle.
    /// </summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>
    /// Controls visibility of the subtitle line in WPF templates (only shown if explicit subtitle exists).
    /// </summary>
    public Visibility SubtitleVisibility => HasSubtitle ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Controls visibility of keyboard shortcut badge in WPF templates.
    /// </summary>
    public Visibility ShortcutVisibility => !string.IsNullOrWhiteSpace(ShortcutHint) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Property for direct WPF data binding of the Segoe Fluent / MDL2 glyph.
    /// </summary>
    public string EffectiveGlyph => GetEffectiveGlyph();

    /// <summary>
    /// Returns Segoe Fluent / MDL2 glyph matching this action.
    /// </summary>
    public string GetEffectiveGlyph()
    {
        if (!string.IsNullOrEmpty(IconGlyph))
        {
            return IconGlyph;
        }

        string key = (IconKey ?? string.Empty).ToLowerInvariant();
        string labelKey = (LabelKey ?? string.Empty).ToLowerInvariant();

        if (key.Contains("paste") || labelKey.Contains("paste")) return "\uE77F"; // Paste
        if (key.Contains("download") || labelKey.Contains("update")) return "\uE896"; // Download
        if (key.Contains("save") || labelKey.Contains("save")) return "\uE74E"; // Save
        if (key.Contains("folder") || labelKey.Contains("folder")) return "\uE8B7"; // Folder
        if (key.Contains("file") || key.Contains("documenttext") || labelKey.Contains("path")) return "\uE8A5"; // Document
        if (key.Contains("image") || labelKey.Contains("image")) return "\uEB9F"; // Picture
        if (key.Contains("person") || key.Contains("contact") || labelKey.Contains("domain")) return "\uE77B"; // Contact
        if (key.Contains("calendar") || labelKey.Contains("date")) return "\uE787"; // Calendar
        if (key.Contains("clock") || key.Contains("time")) return "\uE823"; // Clock
        if (key.Contains("globe") || key.Contains("iso") || key.Contains("browser") || labelKey.Contains("open")) return "\uE774"; // Globe
        if (key.Contains("color") || labelKey.Contains("palette") || labelKey.Contains("designer")) return "\uE790"; // Color / Palette
        if (key.Contains("convert") || key.Contains("dial") || labelKey.Contains("convert") || labelKey.Contains("hsl") || labelKey.Contains("rgb")) return "\uE8AB"; // Switch / Convert
        if (key.Contains("star") || labelKey.Contains("star") || labelKey.Contains("favorite")) return "\uE734"; // FavoriteStar
        if (key.Contains("pin")) return "\uE718"; // Pin
        if (key.Contains("info") || labelKey.Contains("stats")) return "\uE9D2"; // Analytics / Stats
        if (key.Contains("link") || key.Contains("url")) return "\uE71B"; // Link
        if (key.Contains("mail") || key.Contains("email")) return "\uE715"; // Mail
        if (key.Contains("compress") || labelKey.Contains("minify")) return "\uE73F"; // BackToWindow / Minify
        if (key.Contains("code") || key.Contains("json") || key.Contains("braces") || labelKey.Contains("indent") || labelKey.Contains("format")) return "\uE943"; // Code
        if (key.Contains("qr")) return "\uED14"; // QrCode
        if (key.Contains("clean") || key.Contains("cut") || labelKey.Contains("clean") || labelKey.Contains("trim")) return "\uEA99"; // EraseTool / Clean
        if (key.Contains("key") || key.Contains("jwt") || labelKey.Contains("jwt")) return "\uE8D7"; // Permissions / Key
        if (key.Contains("decode") || key.Contains("unlock") || labelKey.Contains("decode")) return "\uE785"; // Unlock / Decode
        if (key.Contains("uppercase") || key.Contains("lowercase") || key.Contains("case") || labelKey.Contains("case") || labelKey.Contains("upper") || labelKey.Contains("lower")) return "\uE8D2"; // Font / Case
        if (key.Contains("checkmark") || labelKey.Contains("validate")) return "\uE73E"; // Checkmark
        if (key.Contains("table") || key.Contains("csv") || key.Contains("tsv") || key.Contains("bullet")) return "\uE8FD"; // BulletedList / Table

        return "\uE8C8"; // Default Copy glyph
    }
}
