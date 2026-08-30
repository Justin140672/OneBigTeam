namespace HR.Infrastructure.Email;

/// <summary>
/// Best-effort, dependency-free parsing of a raw HTTP User-Agent string into friendly
/// browser / operating-system names for display in transactional emails (e.g. the
/// password-reset email's "browser_name" / "operating_system" fields).
///
/// This is presentation only — it is deliberately NOT used for any security decision. Unknown or
/// missing input yields "Unknown".
/// </summary>
public readonly record struct UserAgentSummary(string BrowserName, string OperatingSystem)
{
    public const string Unknown = "Unknown";

    public static UserAgentSummary Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return new UserAgentSummary(Unknown, Unknown);

        var ua = userAgent;

        return new UserAgentSummary(DetectBrowser(ua), DetectOs(ua));
    }

    private static bool Has(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string DetectBrowser(string ua)
    {
        // Order matters: Edge and Chrome both contain "Chrome"; Chrome and Safari both contain "Safari".
        if (Has(ua, "Edg/") || Has(ua, "Edge/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/"))
            return "Edge";
        if (Has(ua, "OPR/") || Has(ua, "Opera"))
            return "Opera";
        if (Has(ua, "Firefox/") || Has(ua, "FxiOS/"))
            return "Firefox";
        if (Has(ua, "CriOS/") || Has(ua, "Chrome/") || Has(ua, "Chromium/"))
            return "Chrome";
        if (Has(ua, "Safari/") && Has(ua, "Version/"))
            return "Safari";

        return Unknown;
    }

    private static string DetectOs(string ua)
    {
        if (Has(ua, "Windows"))
            return "Windows";
        if (Has(ua, "iPhone") || Has(ua, "iPad") || Has(ua, "iPod"))
            return "iOS";
        if (Has(ua, "Android"))
            return "Android";
        if (Has(ua, "Mac OS X") || Has(ua, "Macintosh"))
            return "macOS";
        if (Has(ua, "CrOS"))
            return "Linux";
        if (Has(ua, "Linux") || Has(ua, "X11"))
            return "Linux";

        return Unknown;
    }
}
