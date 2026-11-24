using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ClipFlyout.Native;
using Microsoft.Win32;
using QRCoder;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipFlyout.Services;

public class ActionExecutor
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly Action<string> _setClipboardText;

    public event Action<string>? ActionExecuted;
    public event Action<string, string>? TransformedActionExecuted;
    public event Action<string>? ActionFailed;
    public event Action? BeforeModalDialog;
    public event Action<object>? ClipboardUpdatedInternally;

    public ActionExecutor(IClipboardMonitor clipboardMonitor, Action<string>? setClipboardText = null)
    {
        _clipboardMonitor = clipboardMonitor;
        _setClipboardText = setClipboardText ?? SetClipboardTextWithRetry;
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                WpfClipboard.SetDataObject(text, true);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(50);
            }
        }
        if (lastError != null) throw lastError;
    }

    private static void SetClipboardImageWithRetry(BitmapSource image)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                WpfClipboard.SetImage(image);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(50);
            }
        }
        if (lastError != null) throw lastError;
    }

    public void CopyText(string text, string successMessageKey = "Toast_Copied")
    {
        try
        {
            using (_clipboardMonitor.SuppressNotifications())
            {
                _setClipboardText(text);
            }
            ClipboardUpdatedInternally?.Invoke(text);
            ActionExecuted?.Invoke(_loc.Get(successMessageKey));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void CopyTransformedText(string text, string label)
    {
        try
        {
            using (_clipboardMonitor.SuppressNotifications())
            {
                _setClipboardText(text);
            }
            ClipboardUpdatedInternally?.Invoke(text);
            TransformedActionExecuted?.Invoke(text, label);
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void PasteToActiveWindow(bool notifyToast = false, IntPtr targetHwnd = default)
    {
        try
        {
            bool sent = Win32.PasteToActiveWindow(targetHwnd);
            if (notifyToast)
            {
                ActionExecuted?.Invoke(sent ? _loc.Get("Toast_Pasted") : _loc.Get("Toast_PasteBlocked"));
            }
        }
        catch (Exception ex)
        {
            if (notifyToast)
            {
                ActionExecuted?.Invoke($"Error: {ex.Message}");
            }
        }
    }

    public void CopyImage(BitmapSource image, string successMessageKey = "Toast_Copied")
    {
        try
        {
            using (_clipboardMonitor.SuppressNotifications())
            {
                SetClipboardImageWithRetry(image);
            }
            ActionExecuted?.Invoke(_loc.Get(successMessageKey));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void CopyBase64Image(byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            CopyImage(bitmap, "Toast_Copied");
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Image Decode Error: {ex.Message}");
        }
    }

    public void OpenBrowser(string url)
    {
        try
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ActionExecuted?.Invoke(_loc.Get("Toast_InvalidUrl"));
                return;
            }

            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            ActionExecuted?.Invoke(_loc.Get("Toast_BrowserOpened"));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void OpenEmail(string emailAddress)
    {
        try
        {
            string clean = (emailAddress ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(clean) || clean.Contains('?') || clean.Contains('&') || clean.Contains('\r') || clean.Contains('\n'))
            {
                return;
            }

            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = $"mailto:{Uri.EscapeDataString(clean)}",
                UseShellExecute = true
            });
            ActionExecuted?.Invoke(_loc.Get("Toast_EmailOpened"));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void OpenContainingFolder(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (Directory.Exists(filePath))
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            else if (File.Exists(filePath))
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    using var _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    return;
                }
            }

            ActionExecuted?.Invoke(_loc.Get("Toast_FolderOpened"));
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Error: {ex.Message}");
        }
    }

    public void GenerateAndCopyQrCode(string text)
    {
        _ = Task.Run(() =>
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
            return bitmapImage;
        }).ContinueWith(t =>
        {
            var app = WpfApplication.Current;
            void Deliver()
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    CopyImage(t.Result, "Toast_QrCopied");
                }
                else
                {
                    ActionExecuted?.Invoke($"QR Error: {t.Exception?.GetBaseException().Message}");
                }
            }

            if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.BeginInvoke(Deliver);
            }
            else
            {
                Deliver();
            }
        });
    }

    public void SaveImageToFile(BitmapSource image)
    {
        try
        {
            // Close flyout & uninstall global hooks BEFORE modal SaveFileDialog opens
            BeforeModalDialog?.Invoke();

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                DefaultExt = ".png",
                FileName = $"ClipFlyout_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() == true)
            {
                string fileName = dialog.FileName;
                BitmapEncoder encoder = Path.GetExtension(fileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                encoder.Frames.Add(BitmapFrame.Create(image));
                // Use File.Create (truncates existing file instead of leaving trailing garbage bytes!)
                using var fs = File.Create(fileName);
                encoder.Save(fs);

                ActionExecuted?.Invoke(_loc.Get("Toast_ImageSaved", Path.GetFileName(fileName)));
            }
        }
        catch (Exception ex)
        {
            ActionExecuted?.Invoke($"Save Error: {ex.Message}");
        }
    }

    public async Task ExtractTextFromImageAsync(BitmapSource image)
    {
        try
        {
            string? text = await OcrService.ExtractTextAsync(image);
            void DeliverSuccess(string extracted) => CopyTransformedText(extracted, _loc.Get("Toast_OcrCopied"));
            void DeliverEmpty() => ActionFailed?.Invoke(_loc.Get("Toast_OcrEmpty"));

            var app = WpfApplication.Current;
            void Dispatch()
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    DeliverSuccess(text);
                }
                else
                {
                    DeliverEmpty();
                }
            }

            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess() && !app.Dispatcher.HasShutdownStarted)
            {
                await app.Dispatcher.InvokeAsync(Dispatch);
            }
            else
            {
                Dispatch();
            }
        }
        catch (Exception ex)
        {
            ActionFailed?.Invoke($"Error: {ex.Message}");
        }
    }

    public async Task ExtractTextFromImageBytesAsync(byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            await ExtractTextFromImageAsync(bitmap);
        }
        catch (Exception ex)
        {
            ActionFailed?.Invoke($"Image Decode Error: {ex.Message}");
        }
    }

    public void OpenTerminal(string path)
    {
        try
        {
            string? dir = DeveloperToolsHelper.ResolveExistingDirectory(path);
            if (string.IsNullOrEmpty(dir))
            {
                ActionFailed?.Invoke(_loc.Get("Toast_TerminalDirMissing"));
                return;
            }

            string wtPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "wt.exe");

            ProcessStartInfo psi;
            if (File.Exists(wtPath))
            {
                psi = new ProcessStartInfo { FileName = wtPath, UseShellExecute = false };
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(dir);
            }
            else
            {
                psi = new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false };
                psi.ArgumentList.Add("-NoExit");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add($"Set-Location -LiteralPath '{dir.Replace("'", "''")}'");
            }

            using var _ = Process.Start(psi);
            ActionExecuted?.Invoke(_loc.Get("Toast_TerminalOpened"));
        }
        catch (Exception ex)
        {
            ActionFailed?.Invoke($"Error: {ex.Message}");
        }
    }

    public async Task CopyFileSha256Async(string filePath)
    {
        try
        {
            string? hash = await Task.Run(() => DeveloperToolsHelper.ComputeFileSha256(filePath));
            var app = WpfApplication.Current;
            void Deliver()
            {
                if (!string.IsNullOrEmpty(hash))
                {
                    CopyTransformedText(hash, _loc.Get("Toast_Sha256Copied"));
                }
                else
                {
                    ActionFailed?.Invoke(_loc.Get("Toast_Sha256Unavailable"));
                }
            }

            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess() && !app.Dispatcher.HasShutdownStarted)
            {
                await app.Dispatcher.InvokeAsync(Deliver);
            }
            else
            {
                Deliver();
            }
        }
        catch (Exception ex)
        {
            ActionFailed?.Invoke($"Error: {ex.Message}");
        }
    }
}
