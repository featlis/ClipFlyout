using System.Windows.Media;
using ClipFlyout.Services;
using Xunit;

namespace ClipFlyout.Tests;

public class ThemeHarmonyTests
{
    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    [Fact]
    public void HarmonizeAccentColor_LightMode_DeepensBrightCyanForCrispContrast()
    {
        var brightCyan = Color.FromRgb(0x06, 0xB6, 0xCE);
        var harmonized = ThemeService.HarmonizeAccentColor(brightCyan, isDark: false);

        // Must be darker than raw cyan so it's legible on white cards with crisp white text
        Assert.True(RelativeLuminance(harmonized) < RelativeLuminance(brightCyan));
        Assert.Equal(Color.FromRgb(255, 255, 255), ThemeService.GetContrastingForeground(harmonized));
    }

    [Fact]
    public void HarmonizeAccentColor_DarkMode_LiftsDeepBlueIntoLuminousWin11Tone()
    {
        var deepBlue = Color.FromRgb(0x00, 0x78, 0xD4);
        var harmonized = ThemeService.HarmonizeAccentColor(deepBlue, isDark: true);

        // Must be brighter than raw #0078D4 so it pops cleanly on dark #202020 surfaces
        Assert.True(RelativeLuminance(harmonized) > RelativeLuminance(deepBlue));
        Assert.True(harmonized.B > harmonized.R);
    }

    [Theory]
    [InlineData(255, 230, 0)]   // Neon yellow
    [InlineData(20, 20, 20)]    // Near-black grayscale
    [InlineData(240, 240, 240)] // Near-white grayscale
    [InlineData(139, 92, 246)]  // Purple
    public void HarmonizeAccentColor_ProducesDistinctLegibleTonesAcrossModes(byte r, byte g, byte b)
    {
        var raw = Color.FromRgb(r, g, b);
        var darkTone = ThemeService.HarmonizeAccentColor(raw, isDark: true);
        var lightTone = ThemeService.HarmonizeAccentColor(raw, isDark: false);

        // Dark mode harmonized tone should always be lighter than Light mode harmonized tone
        Assert.True(RelativeLuminance(darkTone) > RelativeLuminance(lightTone));
    }
}
