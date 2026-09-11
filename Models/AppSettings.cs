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

public enum WidgetPositionMode
{
    TrayLeft,
    CenterRight,
    CenterLeft,
    FarLeft,
    AboveTaskbar
}

public enum WidgetTextColorMode
{
    Auto,
    Light,
    Dark
}

public enum WidgetMonitorTarget
{
    Primary,
    FollowCursor,
    Monitor1,
    Monitor2,
    Monitor3
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
    public bool ShowTaskbarWidget { get; set; } = true;
    public WidgetPositionMode WidgetPosition { get; set; } = WidgetPositionMode.TrayLeft;
    public bool WidgetAutoAlign { get; set; } = true;
    public WidgetMonitorTarget WidgetMonitor { get; set; } = WidgetMonitorTarget.Primary;
    public double WidgetOffsetX { get; set; } = 0.0;
    public WidgetTextColorMode WidgetTextColor { get; set; } = WidgetTextColorMode.Auto;
    public bool IgnorePasswordManagers { get; set; } = true;
    public bool EnableRecallHotkey { get; set; } = true;
    public bool IsFirstRun { get; set; } = true;

    // Flyout Visuals & Behavior
    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.BottomRight;
    public double DisplayDurationSeconds { get; set; } = 3.5;
    public double HoverLeaveDurationSeconds { get; set; } = 1.5;
    public double OpacityPercent { get; set; } = 85.0;
    public string AccentColor { get; set; } = "#0078D4";

    // Updates are downloaded only from this project's GitHub Releases and are
    // verified against the release SHA-256 manifest before they are started.
    public bool AutomaticallyInstallUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

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
    public bool DetectEmail { get; set; } = true;
    public bool EnableCleanUrl { get; set; } = true;
    public bool EnableCaseConverter { get; set; } = true;
    public bool EnableJwtDetector { get; set; } = true;

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

        if (!Enum.IsDefined(normalized.WidgetPosition))
        {
            normalized.WidgetPosition = WidgetPositionMode.TrayLeft;
        }

        if (!Enum.IsDefined(normalized.WidgetMonitor))
        {
            normalized.WidgetMonitor = WidgetMonitorTarget.Primary;
        }

        normalized.WidgetOffsetX = Math.Clamp(normalized.WidgetOffsetX, -800.0, 800.0);
        normalized.OpacityPercent = Math.Clamp(normalized.OpacityPercent, 20.0, 100.0);
        normalized.DisplayDurationSeconds = Math.Clamp(normalized.DisplayDurationSeconds, 1.5, 10.0);
        normalized.HoverLeaveDurationSeconds = Math.Clamp(normalized.HoverLeaveDurationSeconds, 0.5, 5.0);
        if (!Enum.IsDefined(normalized.WidgetTextColor))
        {
            normalized.WidgetTextColor = WidgetTextColorMode.Auto;
        }

        if (string.IsNullOrWhiteSpace(normalized.AccentColor) ||
            !System.Text.RegularExpressions.Regex.IsMatch(normalized.AccentColor, "^#[0-9a-fA-F]{6}$"))
        {
            normalized.AccentColor = "#0078D4";
        }

        // A clock change must not suppress checks indefinitely.
        if (normalized.LastUpdateCheckUtc > DateTimeOffset.UtcNow.AddHours(1))
        {
            normalized.LastUpdateCheckUtc = null;
        }

        return normalized;
    }
}
