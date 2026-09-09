using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ClipFlyout.Services;

public static class CleanUrlHelper
{
    private static readonly HashSet<string> KnownTrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "msclkid", "yclid", "mc_cid", "mc_eid",
        "igshid", "twclid", "ttclid", "wbraid", "gbraid",
        "_ga", "_gl", "ref_src", "spm",
        "si", "feature", "scm", "share_id", "tracking_id"
    };

    public static bool IsTrackingKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) return true;
        if (key.StartsWith("ref_", StringComparison.OrdinalIgnoreCase)) return true;
        return KnownTrackingKeys.Contains(key);
    }

    public static bool TryCleanUrl(string? url, out string cleanUrl)
    {
        cleanUrl = url ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.IsNullOrEmpty(uri.Query) || uri.Query == "?")
        {
            return false;
        }

        string rawQuery = uri.Query.TrimStart('?');
        var queryParams = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var retained = new List<string>();
        bool strippedAny = false;

        foreach (var param in queryParams)
        {
            int eqIdx = param.IndexOf('=');
            string key = eqIdx >= 0 ? param[..eqIdx] : param;

            if (IsTrackingKey(key))
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

        var builder = new UriBuilder(uri)
        {
            Query = retained.Count > 0 ? string.Join("&", retained) : string.Empty
        };

        cleanUrl = builder.Uri.ToString();
        return true;
    }
}
