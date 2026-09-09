using System;
using ClipFlyout.Native;
using Microsoft.Win32;

namespace ClipFlyout.Services;

/// <summary>
/// Service to detect Windows taskbar color and luminance to ensure optimal widget text contrast.
/// </summary>
public static class TaskbarColorDetector
{
    private const uint CLR_INVALID = 0xFFFFFFFF;
    public const double LightLuminanceThreshold = 130.0;

    /// <summary>
    /// Calculates standard ITU-R BT.601 perceived luminance (0..255).
    /// </summary>
    public static double CalculateLuminance(byte r, byte g, byte b)
    {
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    /// <summary>
    /// Checks whether the given luminance value corresponds to a light background.
    /// </summary>
    public static bool IsLuminanceLight(double luminance)
    {
        return luminance >= LightLuminanceThreshold;
    }

    /// <summary>
    /// Detects whether the taskbar background is light (high luminance).
    /// Samples pixels on screen near the specified widget coordinates, or defaults to taskbar center.
    /// Falls back to Windows theme registry if pixel sampling cannot be performed.
    /// </summary>
    public static bool IsTaskbarLight(double? screenX = null, double? screenY = null)
    {
        if (TrySampleTaskbarLuminance(screenX, screenY, out double luminance))
        {
            return IsLuminanceLight(luminance);
        }

        return GetRegistryTaskbarIsLight();
    }

    /// <summary>
    /// Samples pixels from the taskbar area to calculate the average background luminance.
    /// </summary>
    public static bool TrySampleTaskbarLuminance(double? screenX, double? screenY, out double averageLuminance)
    {
        averageLuminance = 0;
        IntPtr taskbarHwnd = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!Win32.GetWindowRect(taskbarHwnd, out Win32.RECT tbRect))
        {
            return false;
        }

        IntPtr hdc = Win32.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            int tbCenterX = (tbRect.Left + tbRect.Right) / 2;
            int tbCenterY = (tbRect.Top + tbRect.Bottom) / 2;

            int targetX = screenX.HasValue ? (int)screenX.Value : tbCenterX;
            int targetY = screenY.HasValue ? (int)screenY.Value : tbCenterY;

            // Clamp sample points within the taskbar bounds
            targetX = Math.Clamp(targetX, tbRect.Left + 5, Math.Max(tbRect.Left + 5, tbRect.Right - 5));
            targetY = Math.Clamp(targetY, tbRect.Top + 5, Math.Max(tbRect.Top + 5, tbRect.Bottom - 5));

            // Sample multiple points (center, and slightly left and right) to reduce noise
            int[] xOffsets = { -15, 0, 15 };
            double totalLum = 0;
            int validSamples = 0;

            foreach (int offset in xOffsets)
            {
                int sx = Math.Clamp(targetX + offset, tbRect.Left + 2, Math.Max(tbRect.Left + 2, tbRect.Right - 2));
                uint pixel = Win32.GetPixel(hdc, sx, targetY);
                if (pixel != CLR_INVALID)
                {
                    byte r = (byte)(pixel & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);
                    totalLum += CalculateLuminance(r, g, b);
                    validSamples++;
                }
            }

            if (validSamples > 0)
            {
                averageLuminance = totalLum / validSamples;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Reads Windows shell theme registry keys to determine if the taskbar is configured as light.
    /// </summary>
    public static bool GetRegistryTaskbarIsLight()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            
            // Check if accent color is applied to taskbar
            var colorPrevalence = personalizeKey?.GetValue("ColorPrevalence");
            if (colorPrevalence is int cp && cp == 1)
            {
                // Accent color on taskbar: check luminance of DWM colorization
                using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                var colorization = dwmKey?.GetValue("ColorizationColor");
                if (colorization is int colorVal)
                {
                    byte r = (byte)((colorVal >> 16) & 0xFF);
                    byte g = (byte)((colorVal >> 8) & 0xFF);
                    byte b = (byte)(colorVal & 0xFF);
                    double lum = CalculateLuminance(r, g, b);
                    return IsLuminanceLight(lum);
                }
            }

            // Standard Windows shell theme: SystemUsesLightTheme governs taskbar
            var sysValue = personalizeKey?.GetValue("SystemUsesLightTheme");
            if (sysValue is int sysInt)
            {
                return sysInt != 0;
            }

            // Fallback to AppsUseLightTheme if SystemUsesLightTheme is absent
            var appsValue = personalizeKey?.GetValue("AppsUseLightTheme");
            if (appsValue is int appsInt)
            {
                return appsInt != 0;
            }
        }
        catch
        {
        }

        // Default to dark taskbar (Windows 10/11 default when not explicitly light)
        return false;
    }
}
