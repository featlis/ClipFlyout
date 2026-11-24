using System;

namespace ClipFlyout;

public static class AppInfo
{
    public static Version Version { get; } = typeof(AppInfo).Assembly.GetName().Version ?? new Version(2, 0, 1);
    public static string VersionString { get; } = $"{Version.Major}.{Version.Minor}.{Version.Build}";
    public static string DisplayVersion { get; } = $"v{VersionString}";
}
