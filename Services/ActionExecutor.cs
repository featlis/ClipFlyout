using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QRCoder;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipFlyout.Services;

public class ActionExecutor
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public event Action<string>? ActionExecuted;

    public ActionExecutor(IClipboardMonitor clipboardMonitor)
    {
        _clipboardMonitor = clipboardMonitor;
    }

    public void CopyText(string text, string successMessageKey = "Toast_Copied")
    {
        try
        {
            using (_clipboardMonitor.SuppressNotifications())
            {
                WpfClipboard.SetDataObject(text, true);
            }
            ActionExecuted?.Invoke(_loc.Get(successMessageKey));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void CopyImage(BitmapSource image, string successMessageKey = "Toast_Copied")
    {
        try
        {
            using (_clipboardMonitor.SuppressNotifications())
            {
                WpfClipboard.SetImage(image);
            }
            ActionExecuted?.Invoke(_loc.Get(successMessageKey));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            ActionExecuted?.Invoke(_loc.Get("Toast_BrowserOpened"));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void GenerateAndCopyQrCode(string text)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(20);

            using var ms = new MemoryStream(qrCodeBytes);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            CopyImage(bitmapImage, "Toast_QrCopied");
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"QR Error: {ex.Message}");
        }
    }

    public void SaveImageToFile(BitmapSource image)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                DefaultExt = ".png",
                FileName = $"ClipFlyout_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() == true)
            {
                BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                encoder.Frames.Add(BitmapFrame.Create(image));
                using var fs = File.OpenWrite(dialog.FileName);
                encoder.Save(fs);

                ActionExecuted?.Invoke(_loc.Get("Toast_ImageSaved", Path.GetFileName(dialog.FileName)));
            }
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Save Error: {ex.Message}");
        }
    }
}
