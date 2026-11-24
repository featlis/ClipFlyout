using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipFlyout.Services;

public enum DetectedPathKind
{
    WindowsBackslash,
    WindowsForwardSlash,
    Unc,
    WslMount
}

public readonly record struct PathInfo(
    string CleanPath,
    DetectedPathKind Kind,
    string ForwardSlashPath,
    string BackslashPath,
    string EscapedBackslashPath,
    string? WslPath,
    string? WindowsFromWslPath,
    string? ExistingDirectory);

public readonly record struct MathEvalResult(
    double Value,
    string FormattedValue,
    bool IsIntegral,
    long? ExactInt64,
    string? HexValue,
    string? BinaryValue,
    bool IsBaseLiteralOnly);

/// <summary>
/// Pure helper routines for developer utilities: path conversion, safe math/hex/binary
/// evaluation, UUID formatting/generation, SHA-256 hashing, Base64 encoding, and clip join.
/// </summary>
public static partial class DeveloperToolsHelper
{
    [GeneratedRegex(@"^[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\?)*$")]
    private static partial Regex WindowsBackslashPathRegex();

    [GeneratedRegex(@"^[a-zA-Z]:/(?:[^\\/:*?""<>|\r\n]+/?)+$")]
    private static partial Regex WindowsForwardPathRegex();

    [GeneratedRegex(@"^\\\\[a-zA-Z0-9._-]+\\[^\r\n*?""<>|]+$")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"^/mnt/(?<drive>[a-zA-Z])(?:/(?<rest>[^\r\n\0]*))?$")]
    private static partial Regex WslMountPathRegex();

    [GeneratedRegex(@"^0[xX](?<hex>[0-9a-fA-F]{1,16})$")]
    private static partial Regex HexLiteralRegex();

    [GeneratedRegex(@"^0[bB](?<bin>[01]{2,64})$")]
    private static partial Regex BinaryLiteralRegex();

    [GeneratedRegex(@"^\d{4}[-/]\d{1,2}(?:[-/]\d{1,2})?$")]
    private static partial Regex IsoLikeDateOnlyRegex();

    [GeneratedRegex(@"^\d{1,2}[-/]\d{1,2}(?:[-/]\d{2,4})?$")]
    private static partial Regex ShortDateSlashOrHyphenRegex();

    [GeneratedRegex(@"^\+?\d[\d\s-]{5,}\d$")]
    private static partial Regex PhoneOrSerialRegex();

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex DottedVersionOrIpRegex();

    [GeneratedRegex(@"^\d+-\d+$")]
    private static partial Regex TightHyphenPairRegex();

    public static bool TryParsePath(string? rawInput, out PathInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(rawInput)) return false;

        string trimmed = rawInput.Trim();
        if (trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Length > 1024) return false;

        // Strip one balanced quote pair copied via Explorer "Copy as path"
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length < 3 || trimmed.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        DetectedPathKind kind;
        string? wslPath = null;
        string? winFromWsl = null;

        if (WslMountPathRegex().IsMatch(trimmed))
        {
            kind = DetectedPathKind.WslMount;
            winFromWsl = WslToWindowsPath(trimmed);
            wslPath = trimmed;
        }
        else if (UncPathRegex().IsMatch(trimmed))
        {
            kind = DetectedPathKind.Unc;
        }
        else if (WindowsBackslashPathRegex().IsMatch(trimmed))
        {
            kind = DetectedPathKind.WindowsBackslash;
            wslPath = WindowsToWslPath(trimmed);
        }
        else if (WindowsForwardPathRegex().IsMatch(trimmed))
        {
            kind = DetectedPathKind.WindowsForwardSlash;
            wslPath = WindowsToWslPath(trimmed);
        }
        else
        {
            return false;
        }

        string backslashPath = kind == DetectedPathKind.WslMount
            ? (winFromWsl ?? trimmed)
            : trimmed.Replace('/', '\\');
        string forwardPath = backslashPath.Replace('\\', '/');
        string escapedBackslashPath = ToEscapedBackslashes(backslashPath);
        // Avoid eager SMB network probing for UNC paths during clipboard monitoring
        string? existingDir = kind == DetectedPathKind.Unc ? null : ResolveExistingDirectory(trimmed);

        info = new PathInfo(
            trimmed,
            kind,
            forwardPath,
            backslashPath,
            escapedBackslashPath,
            wslPath,
            winFromWsl,
            existingDir);
        return true;
    }

    public static string ToForwardSlashes(string path) => path.Replace('\\', '/');

    public static string ToEscapedBackslashes(string path) => path.Replace('/', '\\').Replace("\\", "\\\\");

    public static string? WindowsToWslPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[1] != ':' || !char.IsAsciiLetter(path[0]))
        {
            return null;
        }

        char drive = char.ToLowerInvariant(path[0]);
        string rest = path[2..].Replace('\\', '/').TrimStart('/');
        return string.IsNullOrEmpty(rest) ? $"/mnt/{drive}" : $"/mnt/{drive}/{rest}";
    }

    public static string? WslToWindowsPath(string path)
    {
        var m = WslMountPathRegex().Match(path.Trim());
        if (!m.Success) return null;

        char drive = char.ToUpperInvariant(m.Groups["drive"].Value[0]);
        string rest = m.Groups["rest"].Success ? m.Groups["rest"].Value.Replace('/', '\\').TrimEnd('\\') : string.Empty;
        return string.IsNullOrEmpty(rest) ? $"{drive}:\\" : $"{drive}:\\{rest}";
    }

    public static string? ResolveExistingDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string candidate = path.Trim();
            if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
            {
                candidate = candidate[1..^1].Trim();
            }

            if (WslMountPathRegex().IsMatch(candidate))
            {
                candidate = WslToWindowsPath(candidate) ?? candidate;
            }
            else
            {
                candidate = candidate.Replace('/', '\\');
            }

            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(Path.GetFullPath(candidate));
            }
        }
        catch
        {
        }
        return null;
    }

    public static bool TryParseUuid(string? rawInput, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rawInput)) return false;

        string trimmed = rawInput.Trim();
        if (trimmed.Length is < 36 or > 38) return false;

        return Guid.TryParseExact(trimmed, "D", out guid) ||
               Guid.TryParseExact(trimmed, "B", out guid) ||
               Guid.TryParseExact(trimmed, "P", out guid);
    }

    public static string NewUuidV4() => Guid.NewGuid().ToString("D");

    public static string NewUuidV7() => Guid.CreateVersion7().ToString("D");

    public static string ComputeUtf8Sha256(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }

    public static string? ComputeFileSha256(string filePath, long maxBytes = 256L * 1024 * 1024)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            var fi = new FileInfo(filePath);
            if (fi.Length > maxBytes) return null;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = SHA256.HashData(fs);
            return Convert.ToHexStringLower(hash);
        }
        catch
        {
            return null;
        }
    }

    public static string EncodeUtf8Base64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public static string JoinClips(string previous, string current, string separator = "\n") =>
        $"{previous}{separator}{current}";

    public static bool TryEvaluateMath(string? rawInput, out MathEvalResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(rawInput)) return false;

        string expr = rawInput.Trim();
        if (expr.Length > 128 || expr.Contains('\r') || expr.Contains('\n')) return false;

        if (expr.EndsWith('='))
        {
            expr = expr[..^1].TrimEnd();
        }
        if (string.IsNullOrWhiteSpace(expr) || expr.Contains('=')) return false;

        // Anti-false-positive filters: reject dates, phone numbers, versions/IPs, URLs, paths, tight hyphen IDs.
        if (IsoLikeDateOnlyRegex().IsMatch(expr) ||
            ShortDateSlashOrHyphenRegex().IsMatch(expr) ||
            DottedVersionOrIpRegex().IsMatch(expr) ||
            TightHyphenPairRegex().IsMatch(expr) ||
            PhoneOrSerialRegex().IsMatch(expr))
        {
            return false;
        }

        // Reject expressions where only '-' appears without spaces or parentheses (e.g. serials/SKUs/phone tails)
        if (!expr.Any(c => c is '+' or '*' or '/' or '%' or '(' or ')') &&
            expr.Contains('-') &&
            !HexLiteralRegex().IsMatch(expr) &&
            !BinaryLiteralRegex().IsMatch(expr))
        {
            // Count occurrences of '-' cleanly surrounded by digits vs spaced subtraction
            if (!Regex.IsMatch(expr, @"\s-\s"))
            {
                return false;
            }
        }

        // Fast path for standalone hex / binary literal conversion
        var hexOnly = HexLiteralRegex().Match(expr);
        if (hexOnly.Success && ulong.TryParse(hexOnly.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hexVal))
        {
            string formatted = hexVal.ToString(CultureInfo.InvariantCulture);
            long? exactSigned = hexVal <= long.MaxValue ? (long)hexVal : null;
            string? bin = hexVal <= long.MaxValue ? "0b" + Convert.ToString((long)hexVal, 2) : null;
            result = new MathEvalResult(hexVal, formatted, true, exactSigned, $"0x{hexVal:X}", bin, IsBaseLiteralOnly: true);
            return true;
        }

        var binOnly = BinaryLiteralRegex().Match(expr);
        if (binOnly.Success)
        {
            try
            {
                ulong binVal = Convert.ToUInt64(binOnly.Groups["bin"].Value, 2);
                string formatted = binVal.ToString(CultureInfo.InvariantCulture);
                long? exactSigned = binVal <= long.MaxValue ? (long)binVal : null;
                result = new MathEvalResult(binVal, formatted, true, exactSigned, $"0x{binVal:X}", $"0b{binOnly.Groups["bin"].Value}", IsBaseLiteralOnly: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (!TryTokenizeMath(expr, out var tokens, out bool hasBinaryOp) || !hasBinaryOp)
        {
            return false;
        }

        int pos = 0;
        if (!TryParseExpr(tokens, ref pos, out double val) || pos != tokens.Count || !double.IsFinite(val))
        {
            return false;
        }

        bool isIntegral = Math.Abs(val) <= 9_007_199_254_740_991.0 &&
                          Math.Abs(val - Math.Round(val)) < 1e-11;

        long? exactInt = null;
        string formattedVal;
        string? hexRep = null;
        string? binRep = null;

        if (isIntegral)
        {
            long rounded = (long)Math.Round(val);
            exactInt = rounded;
            formattedVal = rounded.ToString(CultureInfo.InvariantCulture);
            if (rounded >= 0)
            {
                hexRep = $"0x{rounded:X}";
                binRep = $"0b{Convert.ToString(rounded, 2)}";
            }
        }
        else
        {
            formattedVal = val.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        result = new MathEvalResult(val, formattedVal, isIntegral, exactInt, hexRep, binRep, IsBaseLiteralOnly: false);
        return true;
    }

    private enum MathTokenKind { Number, Plus, Minus, Mul, Div, Mod, LParen, RParen }
    private readonly record struct MathToken(MathTokenKind Kind, double Value = 0);

    private static bool TryTokenizeMath(string expr, out List<MathToken> tokens, out bool hasBinaryOp)
    {
        tokens = new List<MathToken>();
        hasBinaryOp = false;
        int i = 0;
        bool expectOperand = true;

        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new(MathTokenKind.LParen));
                expectOperand = true;
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new(MathTokenKind.RParen));
                expectOperand = false;
                i++;
                continue;
            }

            if (c is '+' or '-' or '*' or '/' or '%')
            {
                var kind = c switch
                {
                    '+' => MathTokenKind.Plus,
                    '-' => MathTokenKind.Minus,
                    '*' => MathTokenKind.Mul,
                    '/' => MathTokenKind.Div,
                    _ => MathTokenKind.Mod
                };
                if (!expectOperand)
                {
                    hasBinaryOp = true;
                }
                else if (c is not '+' and not '-')
                {
                    return false;
                }

                tokens.Add(new(kind));
                expectOperand = true;
                i++;
                continue;
            }

            // Hex 0x...
            if (c == '0' && i + 2 <= expr.Length && i + 1 < expr.Length && expr[i + 1] is 'x' or 'X')
            {
                int start = i + 2;
                int end = start;
                while (end < expr.Length && Uri.IsHexDigit(expr[end])) end++;
                if (end == start || end - start > 16) return false;
                if (!ulong.TryParse(expr.AsSpan(start, end - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hx))
                {
                    return false;
                }
                tokens.Add(new(MathTokenKind.Number, hx));
                expectOperand = false;
                i = end;
                continue;
            }

            // Binary 0b...
            if (c == '0' && i + 1 < expr.Length && expr[i + 1] is 'b' or 'B')
            {
                int start = i + 2;
                int end = start;
                while (end < expr.Length && expr[end] is '0' or '1') end++;
                if (end == start || end - start > 64) return false;
                try
                {
                    ulong bn = Convert.ToUInt64(expr[start..end], 2);
                    tokens.Add(new(MathTokenKind.Number, bn));
                }
                catch
                {
                    return false;
                }
                expectOperand = false;
                i = end;
                continue;
            }

            // Decimal number
            if (char.IsAsciiDigit(c) || c == '.')
            {
                int start = i;
                bool seenDot = false;
                while (i < expr.Length && (char.IsAsciiDigit(expr[i]) || (!seenDot && expr[i] == '.')))
                {
                    if (expr[i] == '.') seenDot = true;
                    i++;
                }
                string slice = expr[start..i];
                if (slice == "." || !double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                {
                    return false;
                }
                // Reject letter directly touching number (e.g. 12px, 3rem)
                if (i < expr.Length && char.IsLetter(expr[i])) return false;

                tokens.Add(new(MathTokenKind.Number, num));
                expectOperand = false;
                continue;
            }

            return false;
        }

        return tokens.Count > 0 && !expectOperand;
    }

    private static bool TryParseExpr(List<MathToken> tokens, ref int pos, out double val)
    {
        if (!TryParseTerm(tokens, ref pos, out val)) return false;
        while (pos < tokens.Count && tokens[pos].Kind is MathTokenKind.Plus or MathTokenKind.Minus)
        {
            var op = tokens[pos++].Kind;
            if (!TryParseTerm(tokens, ref pos, out double right)) return false;
            val = op == MathTokenKind.Plus ? val + right : val - right;
        }
        return true;
    }

    private static bool TryParseTerm(List<MathToken> tokens, ref int pos, out double val)
    {
        if (!TryParseFactor(tokens, ref pos, out val)) return false;
        while (pos < tokens.Count && tokens[pos].Kind is MathTokenKind.Mul or MathTokenKind.Div or MathTokenKind.Mod)
        {
            var op = tokens[pos++].Kind;
            if (!TryParseFactor(tokens, ref pos, out double right)) return false;
            if (op is MathTokenKind.Div or MathTokenKind.Mod && Math.Abs(right) < 1e-15)
            {
                return false;
            }
            val = op switch
            {
                MathTokenKind.Mul => val * right,
                MathTokenKind.Div => val / right,
                _ => val % right
            };
        }
        return true;
    }

    private static bool TryParseFactor(List<MathToken> tokens, ref int pos, out double val)
    {
        val = 0;
        if (pos >= tokens.Count) return false;

        int sign = 1;
        if (tokens[pos].Kind == MathTokenKind.Plus)
        {
            pos++;
        }
        else if (tokens[pos].Kind == MathTokenKind.Minus)
        {
            sign = -1;
            pos++;
        }

        if (pos >= tokens.Count) return false;

        if (tokens[pos].Kind == MathTokenKind.Number)
        {
            val = sign * tokens[pos++].Value;
            return true;
        }

        if (tokens[pos].Kind == MathTokenKind.LParen)
        {
            pos++;
            if (!TryParseExpr(tokens, ref pos, out double inner)) return false;
            if (pos >= tokens.Count || tokens[pos].Kind != MathTokenKind.RParen) return false;
            pos++;
            val = sign * inner;
            return true;
        }

        return false;
    }
}
