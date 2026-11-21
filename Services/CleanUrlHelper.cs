using System;
using System.Collections.Generic;

namespace ClipFlyout.Services;

public static class CleanUrlHelper
{
    private static readonly HashSet<string> GlobalTrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "msclkid", "yclid", "mc_cid", "mc_eid",
        "igshid", "twclid", "ttclid", "wbraid", "gbraid",
        "_ga", "_gl", "ref_src", "spm", "scm", "share_id", "tracking_id"
    };

    private static readonly HashSet<string> MediaShareDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
        "open.spotify.com", "spotify.com"
    };

    public static bool IsTrackingKey(string key, string? host = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) return true;
        if (key.StartsWith("ref_", StringComparison.OrdinalIgnoreCase)) return true;
        if (GlobalTrackingKeys.Contains(key)) return true;

        // Scope "si" and "feature" to media-sharing domains so legitimate APIs aren't broken
        if ((key.Equals("si", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("feature", StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(host) || MediaShareDomains.Contains(host)))
        {
            return true;
        }

        return false;
    }

    public static bool TryCleanUrl(string? url, out string cleanUrl)
    {
        cleanUrl = url ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        int qIndex = trimmed.IndexOf('?');
        if (qIndex < 0)
        {
            return false;
        }

        int hashIndex = trimmed.IndexOf('#', qIndex);
        string basePart = trimmed[..qIndex];
        string rawQuery = hashIndex >= 0
            ? trimmed[(qIndex + 1)..hashIndex]
            : trimmed[(qIndex + 1)..];
        string fragment = hashIndex >= 0 ? trimmed[hashIndex..] : string.Empty;

        if (string.IsNullOrEmpty(rawQuery))
        {
            return false;
        }

        var queryParams = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var retained = new List<string>(queryParams.Length);
        bool strippedAny = false;

        foreach (var param in queryParams)
        {
            int eqIdx = param.IndexOf('=');
            string key = eqIdx >= 0 ? param[..eqIdx] : param;

            if (IsTrackingKey(key, uri.Host))
            {
                strippedAny = true;
            }
            else
            {
                retained.Add(param);
            }
        }

        if (!strippedAny)
        {
            return false;
        }

        // Reconstruct preserving exact original percent-encoding!
        cleanUrl = retained.Count > 0
            ? $"{basePart}?{string.Join("&", retained)}{fragment}"
            : $"{basePart}{fragment}";

        return true;
    }
}
