using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipFlyout.Services;

/// <summary>
/// Retrieves releases from the project's GitHub Releases page and strictly verifies
/// SHA-256 checksums (fail-closed) before ever launching an installer.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/featlis/ClipFlyout/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());

    public static UpdateService Instance => _instance.Value;

    public async Task<UpdateRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
        {
            return null;
        }
        if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var versionText = tagElement.GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion) || latestVersion <= CurrentVersion)
        {
            return null;
        }

        string? setupUrl = null;
        string? fallbackExeUrl = null;
        string? checksumsUrl = null;
        string? releaseNotesUrl = root.TryGetProperty("html_url", out var htmlUrlElem) && htmlUrlElem.ValueKind == JsonValueKind.String
            ? htmlUrlElem.GetString()
            : null;

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElem.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElem) || nameElem.ValueKind != JsonValueKind.String) continue;
                if (!asset.TryGetProperty("browser_download_url", out var urlElem) || urlElem.ValueKind != JsonValueKind.String) continue;

                string? name = nameElem.GetString();
                string? url = urlElem.GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

                if (name.Equals("checksums-sha256.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumsUrl = url;
                }
                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (name.StartsWith("ClipFlyout-Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        setupUrl = url;
                    }
                    else if (name.StartsWith("ClipFlyout", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackExeUrl ??= url;
                    }
                }
            }
        }

        string? installerUrl = setupUrl ?? fallbackExeUrl;
        return installerUrl is null ? null : new UpdateRelease(latestVersion, installerUrl, checksumsUrl, releaseNotesUrl);
    }

    public async Task DownloadAndStartInstallerAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        // Security: Fail closed if checksum manifest is missing or invalid.
        if (string.IsNullOrWhiteSpace(release.ChecksumsUrl))
        {
            throw new CryptographicException("Release does not provide a SHA-256 checksum manifest.");
        }

        string updatesRoot = Path.Combine(Path.GetTempPath(), "ClipFlyout", "updates");
        CleanupOldUpdates(updatesRoot, release.Version.ToString());

        string tempDirectory = Path.Combine(updatesRoot, release.Version.ToString());
        Directory.CreateDirectory(tempDirectory);
        string installerPath = Path.Combine(tempDirectory, $"ClipFlyout-Setup-v{release.Version}.exe");
        string partialInstallerPath = installerPath + ".partial";

        string checksums = await Client.GetStringAsync(release.ChecksumsUrl, cancellationToken).ConfigureAwait(false);
        string? expectedHash = FindSha256(checksums, Path.GetFileName(installerPath))
            ?? FindSha256(checksums, Path.GetFileName(new Uri(release.InstallerUrl).AbsolutePath));

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new CryptographicException("Installer entry not found in SHA-256 checksum manifest.");
        }

        if (File.Exists(partialInstallerPath)) File.Delete(partialInstallerPath);

        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadCts.CancelAfter(TimeSpan.FromMinutes(5));

        using var downloadResponse = await Client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token).ConfigureAwait(false);
        downloadResponse.EnsureSuccessStatusCode();

        long? totalBytes = downloadResponse.Content.Headers.ContentLength;
        await using (var source = await downloadResponse.Content.ReadAsStreamAsync(downloadCts.Token).ConfigureAwait(false))
        await using (var destination = File.Create(partialInstallerPath))
        {
            byte[] buffer = new byte[81920];
            long readTotal = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, downloadCts.Token).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), downloadCts.Token).ConfigureAwait(false);
                readTotal += bytesRead;
                if (totalBytes is > 0)
                {
                    progress?.Report(Math.Clamp((double)readTotal / totalBytes.Value, 0.0, 1.0));
                }
            }
        }

        string actualHash;
        await using (var file = File.OpenRead(partialInstallerPath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(partialInstallerPath); } catch { }
            throw new CryptographicException("The downloaded update did not match its SHA-256 checksum.");
        }

        File.Move(partialInstallerPath, installerPath, true);

        using var installer = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS",
            UseShellExecute = true
        });
        if (installer is null)
        {
            throw new InvalidOperationException("The update installer could not be started.");
        }
    }

    private static void CleanupOldUpdates(string updatesRoot, string keepVersion)
    {
        try
        {
            if (!Directory.Exists(updatesRoot)) return;
            foreach (var dir in Directory.GetDirectories(updatesRoot))
            {
                if (!Path.GetFileName(dir).Equals(keepVersion, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }
        catch
        {
        }
    }

    internal static Version CurrentVersion => AppInfo.Version;

    internal static string? FindSha256(string manifest, string fileName)
    {
        foreach (string line in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 64 && parts[^1].TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }
        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClipFlyout-Updater/{AppInfo.VersionString}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed record UpdateRelease(Version Version, string InstallerUrl, string? ChecksumsUrl, string? ReleaseNotesUrl = null);
