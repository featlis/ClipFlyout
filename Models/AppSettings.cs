using System;
using ClipFlyout.Services;

namespace ClipFlyout.Models;

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public enum FlyoutPlacement
{
    BottomRight,
    TopRight,
    TopLeft,
    BottomLeft,
    NearCursor
}

/// <summary>
/// User settings model with persistence support.
/// </summary>
public class AppSettings
{
    // General
    public bool IsMonitoringEnabled { get; set; } = true;
    public bool LaunchOnStartup { get; set; } = false;
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    public AppLanguage Language { get; set; } = AppLanguage.Auto;

    // Flyout Visuals & Behavior
    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.BottomRight;
    public double DisplayDurationSeconds { get; set; } = 3.5;
    public double HoverLeaveDurationSeconds { get; set; } = 1.5;
    public double OpacityPercent { get; set; } = 85.0;

    // Updates are downloaded only from this project's GitHub Releases and are
    // verified against the release SHA-256 manifest before they are started.
    public bool AutomaticallyInstallUpdates { get; set; } = true;

    // Data Type Detectors
    public bool DetectHexColor { get; set; } = true;
    public bool DetectJson { get; set; } = true;
    public bool DetectUrl { get; set; } = true;
    public bool DetectCode { get; set; } = true;
    public bool DetectImage { get; set; } = true;
    public bool DetectPlainText { get; set; } = true;
    public bool DetectTimestamp { get; set; } = true;
    public bool DetectBase64 { get; set; } = true;
    public bool DetectTable { get; set; } = true;

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    /// <summary>
    /// Returns a safe copy of settings loaded from disk or supplied by a caller.
    /// Settings files are user-editable, so values must not be trusted just because
    /// they deserialized successfully.
    /// </summary>
    public AppSettings Normalize()
    {
        var normalized = Clone();

        if (!Enum.IsDefined(normalized.Theme))
        {
            normalized.Theme = AppThemeMode.System;
        }

        if (!Enum.IsDefined(normalized.Language))
        {
            normalized.Language = AppLanguage.Auto;
        }

        if (!Enum.IsDefined(normalized.Placement))
        {
            normalized.Placement = FlyoutPlacement.BottomRight;
        }

        normalized.OpacityPercent = Math.Clamp(normalized.OpacityPercent, 20.0, 100.0);
        normalized.DisplayDurationSeconds = Math.Clamp(normalized.DisplayDurationSeconds, 1.5, 10.0);
        normalized.HoverLeaveDurationSeconds = Math.Clamp(normalized.HoverLeaveDurationSeconds, 0.5, 5.0);

        return normalized;
    }
}
