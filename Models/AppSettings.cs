using System;
using System.Text.RegularExpressions;
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
    private static readonly Regex HexColorRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

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
    public string ExcludedApps { get; set; } = string.Empty;
    public bool EnableRecallHotkey { get; set; } = true;
    public uint RecallHotkeyModifiers { get; set; } = 0x0001u | 0x0004u; // MOD_ALT | MOD_SHIFT
    public uint RecallHotkeyKey { get; set; } = 0x43u; // 'C'
    public bool IsFirstRun { get; set; } = true;

    // Flyout Visuals & Behavior
    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.BottomRight;
    public double DisplayDurationSeconds { get; set; } = 4.0;
    public double HoverLeaveDurationSeconds { get; set; } = 1.5;
    public double OpacityPercent { get; set; } = 85.0;
    public string AccentColor { get; set; } = "#0078D4";
    public bool UseSystemAccentColor { get; set; } = false;
    public bool SuppressInFullscreen { get; set; } = true;

    // History
    public bool PersistHistory { get; set; } = false;

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
    public bool DetectIsoDateTime { get; set; } = true;
    public bool DetectBase64 { get; set; } = true;
    public bool DetectTable { get; set; } = true;
    public bool DetectEmail { get; set; } = true;
    public bool DetectFileDrop { get; set; } = true;
    public bool EnableCleanUrl { get; set; } = true;
    public bool EnableCaseConverter { get; set; } = true;
    public bool EnableJwtDetector { get; set; } = true;
    public bool EnableOcr { get; set; } = true;
    public bool DetectFilePath { get; set; } = true;
    public bool DetectMath { get; set; } = true;
    public bool DetectUuid { get; set; } = true;

    public static string FormatHotkeyDisplay(uint modifiers, uint virtualKey)
    {
        var parts = new System.Collections.Generic.List<string>(5);
        if ((modifiers & 0x0002u) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001u) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004u) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008u) != 0) parts.Add("Win");

        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)virtualKey);
        string keyLabel = key switch
        {
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString(),
            System.Windows.Input.Key.OemComma => ",",
            System.Windows.Input.Key.OemPeriod => ".",
            System.Windows.Input.Key.OemMinus => "-",
            System.Windows.Input.Key.OemPlus => "=",
            System.Windows.Input.Key.OemQuestion => "/",
            System.Windows.Input.Key.OemSemicolon => ";",
            System.Windows.Input.Key.OemQuotes => "'",
            System.Windows.Input.Key.OemOpenBrackets => "[",
            System.Windows.Input.Key.OemCloseBrackets => "]",
            System.Windows.Input.Key.OemPipe => "\\",
            System.Windows.Input.Key.OemTilde => "`",
            _ => key.ToString()
        };
        parts.Add(keyLabel);
        return string.Join("+", parts);
    }

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (!double.IsFinite(value)) return fallback;
        return Math.Clamp(value, min, max);
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

        normalized.WidgetOffsetX = ClampFinite(normalized.WidgetOffsetX, -800.0, 800.0, 0.0);
        normalized.OpacityPercent = ClampFinite(normalized.OpacityPercent, 20.0, 100.0, 85.0);
        normalized.DisplayDurationSeconds = ClampFinite(normalized.DisplayDurationSeconds, 1.5, 10.0, 4.0);
        normalized.HoverLeaveDurationSeconds = ClampFinite(normalized.HoverLeaveDurationSeconds, 0.5, 5.0, 1.5);

        if (!Enum.IsDefined(normalized.WidgetTextColor))
        {
            normalized.WidgetTextColor = WidgetTextColorMode.Auto;
        }

        if (string.IsNullOrWhiteSpace(normalized.AccentColor) || !HexColorRegex.IsMatch(normalized.AccentColor))
        {
            normalized.AccentColor = "#0078D4";
        }

        // A clock change must not suppress checks indefinitely.
        if (normalized.LastUpdateCheckUtc > DateTimeOffset.UtcNow.AddHours(1))
        {
            normalized.LastUpdateCheckUtc = null;
        }

        uint validMods = normalized.RecallHotkeyModifiers & (0x0001u | 0x0002u | 0x0004u | 0x0008u);
        if (validMods == 0 || normalized.RecallHotkeyKey == 0 || normalized.RecallHotkeyKey > 0xFE)
        {
            normalized.RecallHotkeyModifiers = 0x0001u | 0x0004u;
            normalized.RecallHotkeyKey = 0x43u;
        }
        else
        {
            normalized.RecallHotkeyModifiers = validMods;
        }

        normalized.ExcludedApps ??= string.Empty;
        if (normalized.ExcludedApps.Length > 2048)
        {
            normalized.ExcludedApps = normalized.ExcludedApps[..2048];
        }

        return normalized;
    }
}
