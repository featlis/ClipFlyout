using System;
using ClipFlyout.Native;
using Microsoft.Win32;

namespace ClipFlyout.Services;

/// <summary>
/// Service to detect Windows taskbar color and luminance to ensure optimal widget text contrast.
/// Uses fast zero-GPU-stall Registry detection by default, with on-demand pixel sampling only when requested.
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
    /// Fast, zero-DWM-stall taskbar light/dark check via Windows shell personalization registry.
    /// </summary>
    public static bool IsTaskbarLightFast()
    {
        return GetRegistryTaskbarIsLight();
    }

    public static bool IsTaskbarLight(double? screenX = null, double? screenY = null)
    {
        if (TrySampleTaskbarLuminance(screenX, screenY, out double luminance))
        {
            return IsLuminanceLight(luminance);
        }

        return GetRegistryTaskbarIsLight();
    }

    public static bool IsTaskbarLight(
        double widgetDipLeft,
        double widgetDipTop,
        double widgetDipWidth,
        double widgetDipHeight,
        double dpiScale,
        bool isAboveTaskbar,
        IntPtr targetTaskbar = default)
    {
        if (!isAboveTaskbar)
        {
            // Avoid expensive synchronous GetPixel GPU stalls for normal taskbar-docked mode
            return GetRegistryTaskbarIsLight();
        }

        if (TrySampleOutsideWidgetLuminance(widgetDipLeft, widgetDipTop, widgetDipWidth, widgetDipHeight, dpiScale, isAboveTaskbar, out double luminance, targetTaskbar))
        {
            return IsLuminanceLight(luminance);
        }

        return GetRegistryTaskbarIsLight();
    }

    public static bool TrySampleOutsideWidgetLuminance(
        double widgetDipLeft,
        double widgetDipTop,
        double widgetDipWidth,
        double widgetDipHeight,
        double dpiScale,
        bool isAboveTaskbar,
        out double averageLuminance,
        IntPtr targetTaskbar = default)
    {
        averageLuminance = 0;
        if (dpiScale <= 0) dpiScale = 1.0;

        int physLeft = (int)Math.Round(widgetDipLeft * dpiScale);
        int physTop = (int)Math.Round(widgetDipTop * dpiScale);
        int physWidth = (int)Math.Round(widgetDipWidth * dpiScale);
        int physHeight = (int)Math.Round(widgetDipHeight * dpiScale);
        int physRight = physLeft + physWidth;
        int physBottom = physTop + physHeight;

        IntPtr hdc = Win32.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            double totalLum = 0;
            int validSamples = 0;

            if (isAboveTaskbar)
            {
                var samplePoints = new (int X, int Y)[]
                {
                    (physLeft - 16, physTop + physHeight / 2),
                    (physRight + 16, physTop + physHeight / 2),
                    (physLeft + physWidth / 2, physTop - 12),
                    (physLeft + physWidth / 2, physBottom + 12)
                };

                foreach (var pt in samplePoints)
                {
                    uint pixel = Win32.GetPixel(hdc, pt.X, pt.Y);
                    if (pixel != CLR_INVALID)
                    {
                        byte r = (byte)(pixel & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte b = (byte)((pixel >> 16) & 0xFF);
                        totalLum += CalculateLuminance(r, g, b);
                        validSamples++;
                    }
                }
            }
            else
            {
                IntPtr taskbarHwnd = targetTaskbar != IntPtr.Zero ? targetTaskbar : Win32.FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd == IntPtr.Zero || !Win32.GetWindowRect(taskbarHwnd, out Win32.RECT tbRect))
                {
                    return false;
                }

                int tbCenterY = (tbRect.Top + tbRect.Bottom) / 2;
                int[] candidateXOffsets = [-15, -30, 15, 30];

                foreach (int offset in candidateXOffsets)
                {
                    int sx = offset < 0 ? physLeft + offset : physRight + offset;
                    if (sx >= tbRect.Left + 5 && sx <= tbRect.Right - 5 && (sx < physLeft || sx > physRight))
                    {
                        uint pixel = Win32.GetPixel(hdc, sx, tbCenterY);
                        if (pixel != CLR_INVALID)
                        {
                            byte r = (byte)(pixel & 0xFF);
                            byte g = (byte)((pixel >> 8) & 0xFF);
                            byte b = (byte)((pixel >> 16) & 0xFF);
                            totalLum += CalculateLuminance(r, g, b);
                            validSamples++;
                        }
                    }
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

    public static bool TrySampleTaskbarLuminance(double? screenX, double? screenY, out double averageLuminance)
    {
        averageLuminance = 0;
        IntPtr taskbarHwnd = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd == IntPtr.Zero || !Win32.GetWindowRect(taskbarHwnd, out Win32.RECT tbRect))
        {
            return false;
        }

        IntPtr hdc = Win32.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            int tbCenterX = (tbRect.Left + tbRect.Right) / 2;
            int tbCenterY = (tbRect.Top + tbRect.Bottom) / 2;

            int targetX = Math.Clamp(screenX.HasValue ? (int)screenX.Value : tbCenterX, tbRect.Left + 5, Math.Max(tbRect.Left + 5, tbRect.Right - 5));
            int targetY = Math.Clamp(screenY.HasValue ? (int)screenY.Value : tbCenterY, tbRect.Top + 5, Math.Max(tbRect.Top + 5, tbRect.Bottom - 5));

            int[] xOffsets = [-15, 0, 15];
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

    public static bool GetRegistryTaskbarIsLight()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            var colorPrevalence = personalizeKey?.GetValue("ColorPrevalence");
            if (colorPrevalence is int cp && cp == 1)
            {
                using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                var colorization = dwmKey?.GetValue("ColorizationColor");
                if (colorization is int colorVal)
                {
                    byte r = (byte)((colorVal >> 16) & 0xFF);
                    byte g = (byte)((colorVal >> 8) & 0xFF);
                    byte b = (byte)(colorVal & 0xFF);
                    return IsLuminanceLight(CalculateLuminance(r, g, b));
                }
            }

            var sysValue = personalizeKey?.GetValue("SystemUsesLightTheme");
            if (sysValue is int sysInt)
            {
                return sysInt != 0;
            }

            var appsValue = personalizeKey?.GetValue("AppsUseLightTheme");
            if (appsValue is int appsInt)
            {
                return appsInt != 0;
            }
        }
        catch
        {
        }

        return false;
    }
}
