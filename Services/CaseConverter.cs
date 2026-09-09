using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClipFlyout.Services;

public static class CaseConverter
{
    private static readonly Regex WordRegex = new(@"[A-Z]+(?![a-z])|[A-Z][a-z]+|[0-9]+|[a-z]+", RegexOptions.Compiled);
    private static readonly Regex ValidCharsRegex = new(@"^[a-zA-Z0-9_\-\s]+$", RegexOptions.Compiled);

    public static bool IsConvertible(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        string trimmed = input.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 80) return false;
        if (trimmed.Contains('\n') || trimmed.Contains('\r')) return false;
        if (!ValidCharsRegex.IsMatch(trimmed)) return false;

        var words = SplitWords(trimmed);
        return words.Count >= 1 && words.Count <= 8 && words.Any(w => w.Any(char.IsLetter));
    }

    public static List<string> SplitWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var matches = WordRegex.Matches(input.Trim());
        var words = new List<string>();
        foreach (Match m in matches)
        {
            if (!string.IsNullOrEmpty(m.Value))
            {
                words.Add(m.Value.ToLowerInvariant());
            }
        }
        return words;
    }

    public static string ToCamelCase(List<string> words)
    {
        if (words.Count == 0) return string.Empty;
        return words[0] + string.Concat(words.Skip(1).Select(Capitalize));
    }

    public static string ToPascalCase(List<string> words)
    {
        return string.Concat(words.Select(Capitalize));
    }

    public static string ToSnakeCase(List<string> words)
    {
        return string.Join("_", words);
    }

    public static string ToKebabCase(List<string> words)
    {
        return string.Join("-", words);
    }

    public static string ToConstantCase(List<string> words)
    {
        return string.Join("_", words).ToUpperInvariant();
    }

    private static string Capitalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;
        if (word.Length == 1) return word.ToUpperInvariant();
        return char.ToUpperInvariant(word[0]) + word[1..];
    }
}
