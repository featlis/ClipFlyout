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

    // Flyout Behavior
    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.BottomRight;
    public double DisplayDurationSeconds { get; set; } = 3.5;
    public double HoverLeaveDurationSeconds { get; set; } = 1.5;

    // Data Type Detectors
    public bool DetectHexColor { get; set; } = true;
    public bool DetectJson { get; set; } = true;
    public bool DetectUrl { get; set; } = true;
    public bool DetectCode { get; set; } = true;
    public bool DetectImage { get; set; } = true;
    public bool DetectPlainText { get; set; } = true;

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }
}
