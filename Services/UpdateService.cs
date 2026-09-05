using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClipFlyout.Services;

/// <summary>
/// Retrieves signed-by-hash releases from the project's GitHub Releases page.
/// The installer is never started until its SHA-256 matches the manifest that
/// was uploaded alongside the release by the release workflow.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/featlis/ClipFlyout/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());

    public static UpdateService Instance => _instance.Value;

    public async Task<UpdateRelease?> CheckForUpdateAsync()
    {
        using var response = await Client.GetAsync(LatestReleaseUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

        var versionText = tagElement.GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion) || latestVersion <= CurrentVersion) return null;

        string? installerUrl = null;
        string? checksumsUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            string? url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (name.Equals("checksums-sha256.txt", StringComparison.OrdinalIgnoreCase)) checksumsUrl = url;
            if (name.Equals($"ClipFlyout-Setup-v{latestVersion}.exe", StringComparison.OrdinalIgnoreCase)) installerUrl = url;
        }

        return installerUrl is null || checksumsUrl is null ? null : new UpdateRelease(latestVersion, installerUrl, checksumsUrl);
    }

    public async Task DownloadAndStartInstallerAsync(UpdateRelease release)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ClipFlyout", "updates", release.Version.ToString());
        Directory.CreateDirectory(tempDirectory);
        string installerPath = Path.Combine(tempDirectory, $"ClipFlyout-Setup-v{release.Version}.exe");
        string partialInstallerPath = installerPath + ".partial";

        string checksums = await Client.GetStringAsync(release.ChecksumsUrl).ConfigureAwait(false);
        string? expectedHash = FindSha256(checksums, Path.GetFileName(installerPath));
        if (expectedHash is null) throw new InvalidDataException("The release checksum does not include the installer.");

        if (File.Exists(partialInstallerPath)) File.Delete(partialInstallerPath);
        using var downloadResponse = await Client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        downloadResponse.EnsureSuccessStatusCode();
        await using (var source = await downloadResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (var destination = File.Create(partialInstallerPath))
        {
            await source.CopyToAsync(destination).ConfigureAwait(false);
        }

        File.Move(partialInstallerPath, installerPath, true);

        string actualHash;
        await using (var file = File.OpenRead(installerPath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file).ConfigureAwait(false));
        }
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(installerPath);
            throw new CryptographicException("The downloaded update did not match its SHA-256 checksum.");
        }

        var installer = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NOCANCEL /CLOSEAPPLICATIONS",
            UseShellExecute = true
        });
        if (installer is null) throw new InvalidOperationException("The update installer could not be started.");
    }

    internal static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    internal static string? FindSha256(string manifest, string fileName)
    {
        foreach (string line in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 64 && parts[^1].Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }
        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipFlyout-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed record UpdateRelease(Version Version, string InstallerUrl, string ChecksumsUrl);
