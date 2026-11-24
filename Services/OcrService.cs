using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ClipFlyout.Services;

/// <summary>
/// On-device Windows Media OCR service (zero external network calls).
/// </summary>
public static partial class OcrService
{
    [GeneratedRegex(@"(?<=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}\u3000-\u303F\uFF00-\uFFEF])[ \t]+(?=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}\u3000-\u303F\uFF00-\uFFEF])")]
    private static partial Regex CjkInterSpaceRegex();

    public static bool IsAvailable() => CreateEngine() is not null;

    private static OcrEngine? CreateEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine != null) return engine;

        foreach (string tag in new[] { "ja", "en-US", "en" })
        {
            try
            {
                var lang = new Language(tag);
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    engine = OcrEngine.TryCreateFromLanguage(lang);
                    if (engine != null) return engine;
                }
            }
            catch
            {
            }
        }

        var firstAvailable = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
        return firstAvailable != null ? OcrEngine.TryCreateFromLanguage(firstAvailable) : null;
    }

    public static string NormalizeCjkSpacing(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return CjkInterSpaceRegex().Replace(text, string.Empty);
    }

    public static async Task<string?> ExtractTextAsync(BitmapSource bitmapSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bitmapSource);

        byte[] pngBytes = EncodeToPng(bitmapSource);

        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            var engine = CreateEngine();
            if (engine == null) return null;

            using var stream = new MemoryStream(pngBytes);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder
                .CreateAsync(stream.AsRandomAccessStream())
                .AsTask(ct)
                .ConfigureAwait(false);
            using var softwareBitmap = await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask(ct)
                .ConfigureAwait(false);

            if (softwareBitmap.PixelWidth == 0 ||
                softwareBitmap.PixelHeight == 0 ||
                softwareBitmap.PixelWidth > OcrEngine.MaxImageDimension ||
                softwareBitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                return null;
            }

            var ocrResult = await engine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            if (ocrResult == null || ocrResult.Lines.Count == 0)
            {
                return string.IsNullOrWhiteSpace(ocrResult?.Text) ? null : NormalizeCjkSpacing(ocrResult.Text).Trim();
            }

            string joined = string.Join(
                Environment.NewLine,
                ocrResult.Lines
                    .Select(l => NormalizeCjkSpacing(l.Text).Trim())
                    .Where(l => !string.IsNullOrEmpty(l)));

            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }, ct).ConfigureAwait(false);
    }

    private static byte[] EncodeToPng(BitmapSource bitmapSource)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
