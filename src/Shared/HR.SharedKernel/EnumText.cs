using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;

namespace HR.SharedKernel;

public static class EnumText
{
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "hr", "cv", "id", "uk", "toil", "csv", "pdf", "mfa", "vat", "url",
    };

    private static readonly HashSet<string> MinorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "or", "and", "the", "a", "an", "to", "for", "in", "on", "at", "by", "vs",
    };

    public static string Humanize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        var parts = trimmed.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1
            ? string.Join(", ", parts.Select(HumanizeSingle))
            : HumanizeSingle(trimmed);
    }

    public static string Humanize(Enum? value)
    {
        if (value is null) return string.Empty;

        var type = value.GetType();
        var name = value.ToString();
        var isFlags = type.IsDefined(typeof(FlagsAttribute), false);
        if (isFlags && name.Contains(", ", StringComparison.Ordinal))
        {
            return string.Join(", ", name.Split(", ").Select(part => HumanizeMember(type, part)));
        }

        return HumanizeMember(type, name);
    }

    public static string ToDisplayText(this Enum? value) => Humanize(value);

    private static string HumanizeMember(Type enumType, string memberName)
    {
        var field = enumType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
        var overrideName = field?.GetCustomAttribute<DisplayAttribute>()?.GetName();
        return string.IsNullOrWhiteSpace(overrideName) ? HumanizeSingle(memberName) : overrideName;
    }

    private static string HumanizeSingle(string value)
    {
        if (value.Length > 0 && char.IsDigit(value[0]) && value.All(c => char.IsDigit(c) || c == '-')) return value;

        var spaced = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '_' or '-' or '.')
            {
                if (spaced.Length > 0 && spaced[^1] != ' ') spaced.Append(' ');
                continue;
            }

            if (i > 0 && value[i - 1] is not ('_' or '-' or '.' or ' ') &&
                ((char.IsUpper(c) &&
                  (char.IsLower(value[i - 1]) ||
                   (char.IsUpper(value[i - 1]) && i + 1 < value.Length && char.IsLower(value[i + 1])))) ||
                 (char.IsDigit(c) && char.IsLower(value[i - 1]))))
            {
                spaced.Append(' ');
            }

            spaced.Append(c);
        }

        var words = spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var isAcronym = word.Length > 1 && word.All(ch => char.IsUpper(ch) || char.IsDigit(ch)) && word.Any(char.IsUpper);
            if (isAcronym) continue;

            if (Acronyms.Contains(word))
            {
                words[i] = word.ToUpperInvariant();
                continue;
            }

            words[i] = i > 0 && MinorWords.Contains(word)
                ? word.ToLowerInvariant()
                : string.Concat(char.ToUpperInvariant(word[0]).ToString(), word.AsSpan(1).ToString().ToLowerInvariant());
        }

        return string.Join(' ', words);
    }
}
