namespace HR.Modules.Companies.Domain;

internal static class CompanySettingsValidation
{
    /// <summary>
    /// Explicit allow-list of supported locales. Deliberately a fixed, curated list rather than
    /// "anything CultureInfo can parse" — SET-01 requires locale values to resolve to an
    /// "explicitly supported culture", not merely a syntactically valid one.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SupportedLocales = new[]
    {
        "en-GB",
        "en-US",
        "en-IE",
        "en-AU",
        "en-CA",
        "en-NZ",
        "fr-FR",
        "de-DE",
        "es-ES",
        "it-IT",
        "nl-NL",
        "pt-PT",
        "ga-IE",
    };

    public static bool IsSupportedLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        return SupportedLocales.Contains(locale.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a time-zone identifier through the application's supported time-zone mechanism
    /// (<see cref="TimeZoneInfo.FindSystemTimeZoneById"/>), returning the canonical id when valid.
    /// This is deliberately platform-tolerant: Windows and IANA identifiers resolve differently
    /// depending on the OS/ICU configuration, so callers should treat a null result as "unsupported"
    /// without assuming a particular identifier family.
    /// </summary>
    public static bool TryResolveTimeZone(string? timeZone, out string canonicalId)
    {
        canonicalId = string.Empty;

        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return false;
        }

        try
        {
            var resolved = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
            canonicalId = resolved.Id;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public const string FallbackTimeZone = "UTC";

    public const string FallbackLocale = "en-GB";
}
