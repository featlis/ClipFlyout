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
    string? IconGlyph = null
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

        if (key.Contains("save") || labelKey.Contains("save")) return "\uE74E"; // Save
        if (key.Contains("calendar") || labelKey.Contains("date")) return "\uE787"; // Calendar
        if (key.Contains("clock") || key.Contains("time")) return "\uE823"; // Clock
        if (key.Contains("globe") || key.Contains("iso")) return "\uE774"; // Globe
        if (key.Contains("color") || labelKey.Contains("palette") || labelKey.Contains("designer")) return "\uE790"; // Color / Palette
        if (key.Contains("dial") || labelKey.Contains("convert") || labelKey.Contains("hsl") || labelKey.Contains("rgb")) return "\uE895"; // Convert / Sync
        if (key.Contains("star") || labelKey.Contains("star") || labelKey.Contains("favorite")) return "\uE734"; // FavoriteStar
        if (key.Contains("info")) return "\uE946"; // Info
        if (key.Contains("open") || key.Contains("link") || key.Contains("url") || key.Contains("browser")) return "\uE71B"; // Link
        if (key.Contains("mail") || key.Contains("email")) return "\uE715"; // Mail
        if (key.Contains("code") || key.Contains("json") || labelKey.Contains("indent") || labelKey.Contains("minify")) return "\uE943"; // Code
        if (key.Contains("qr")) return "\uED14"; // QrCode
        if (key.Contains("clean")) return "\uE894"; // Clear / Broom
        if (key.Contains("table") || key.Contains("csv") || key.Contains("tsv")) return "\uE8A5"; // Document / Table
        if (key.Contains("pin")) return "\uE718"; // Pin

        return "\uE8C8"; // Default Copy glyph
    }
}

