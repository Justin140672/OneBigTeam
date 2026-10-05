using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HR.Modules.Companies.Contracts;

/// <summary>
/// Name normalisation rule: trim and lowercase; strip diacritics (accented letters become their base
/// letter; ß, æ, œ, ø, ł, đ are transliterated); remove apostrophes, hyphens, spaces and every other
/// character that is not a-z or 0-9 (O'Brien becomes obrien, Anne-Marie becomes annemarie).
/// </summary>
public static partial class WorkEmailAddressBuilder
{
    public const int MaxLocalPartLength = 64;
    public const int MaxDomainLength = 253;

    public const string SampleFirstName = "Jane";
    public const string SampleLastName = "Smith";

    public static string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var value = domain.Trim().ToLowerInvariant();
        if (value.StartsWith('@'))
            value = value[1..].Trim();

        return value.Length == 0 ? null : value;
    }

    public static string? ExtractDomain(string? emailAddress)
    {
        var address = emailAddress?.Trim();
        if (string.IsNullOrEmpty(address))
            return null;

        var at = address.IndexOf('@');
        if (at <= 0 || at != address.LastIndexOf('@'))
            return null;

        var domain = NormalizeDomain(address[(at + 1)..]);
        return IsValidDomain(domain) ? domain : null;
    }

    public static WorkEmailNamingConvention InferNamingConvention(string? emailAddress, string? firstName, string? lastName)
    {
        var address = emailAddress?.Trim();
        var at = address?.IndexOf('@') ?? -1;
        if (address is null || at <= 0)
            return WorkEmailNamingConvention.FirstNameDotLastName;

        var localPart = address[..at].ToLowerInvariant();

        foreach (var convention in InferenceOrder)
        {
            if (string.Equals(BuildLocalPart(convention, firstName, lastName), localPart, StringComparison.Ordinal))
                return convention;
        }

        return WorkEmailNamingConvention.FirstNameDotLastName;
    }

    private static readonly WorkEmailNamingConvention[] InferenceOrder =
    [
        WorkEmailNamingConvention.FirstNameDotLastName,
        WorkEmailNamingConvention.FirstInitialDotLastName,
        WorkEmailNamingConvention.FirstNameLastName,
        WorkEmailNamingConvention.FirstName,
    ];

    public static bool IsValidDomain(string? normalizedDomain) =>
        normalizedDomain is not null &&
        normalizedDomain.Length <= MaxDomainLength &&
        DomainPattern().IsMatch(normalizedDomain);

    public static string NormalizeNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            switch (character)
            {
                case 'ß': builder.Append("ss"); continue;
                case 'æ': builder.Append("ae"); continue;
                case 'œ': builder.Append("oe"); continue;
                case 'ø': builder.Append('o'); continue;
                case 'ł': builder.Append('l'); continue;
                case 'đ': builder.Append('d'); continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                builder.Append(character);
        }

        return builder.ToString();
    }

    public static string? BuildLocalPart(WorkEmailNamingConvention convention, string? firstName, string? lastName)
    {
        var first = NormalizeNamePart(firstName);
        var last = NormalizeNamePart(lastName);

        if (first.Length == 0 || last.Length == 0)
            return null;

        var localPart = convention switch
        {
            WorkEmailNamingConvention.FirstNameDotLastName => $"{first}.{last}",
            WorkEmailNamingConvention.FirstInitialDotLastName => $"{first[0]}.{last}",
            WorkEmailNamingConvention.FirstNameLastName => $"{first}{last}",
            WorkEmailNamingConvention.FirstName => first,
            _ => null,
        };

        return localPart is { Length: > 0 and <= MaxLocalPartLength } ? localPart : null;
    }

    public static string? BuildAddress(
        WorkEmailNamingConvention convention,
        string? firstName,
        string? lastName,
        string? normalizedDomain)
    {
        if (!IsValidDomain(normalizedDomain))
            return null;

        var localPart = BuildLocalPart(convention, firstName, lastName);
        return localPart is null ? null : $"{localPart}@{normalizedDomain}";
    }

    [GeneratedRegex(@"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();
}
