namespace HR.SharedKernel.Http;

public static class ReturnUrlValidator
{
    public static string? ValidateInternalPathAllowingBaseRelative(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        var trimmed = returnUrl.Trim();
        return ValidateInternalPath(trimmed.StartsWith('/') ? trimmed : "/" + trimmed);
    }

    public static string? ValidateInternalPath(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        try
        {
            var url = returnUrl.Trim();

            // Must start with exactly one '/' to be application-relative
            // Reject '//' (scheme-relative), empty strings, and non-path starts
            if (!url.StartsWith('/') || url.StartsWith("//"))
                return null;

            // Reject paths with backslashes — some servers treat them as '/',
            // and this pattern is used to bypass path-based filters (e.g. '/\attacker.example')
            if (url.Contains('\\'))
                return null;

            // Reject if it contains an unencoded URI scheme separator ('://')
            // This catches 'http://', 'javascript:', 'data:', etc.
            if (url.Contains("://", StringComparison.OrdinalIgnoreCase))
                return null;

            // Reject encoded variants of scheme separators that could bypass the check above:
            // %3a%2f%2f = :// (case-insensitive hex), %3a// = ://  (mixed hex/literal)
            // These are used to obfuscate URIs while remaining valid in some contexts
            if (url.Contains("%3a%2f%2f", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("%3a//", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("%3A%2F%2F", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("%3A//", StringComparison.OrdinalIgnoreCase))
                return null;

            // Reject partially-encoded schemes (e.g., %3ajavascript: where %3a is an encoded colon)
            // This prevents attackers from obfuscating dangerous schemes with hex encoding
            var lowerUrl = url.ToLowerInvariant();
            var encodedSchemePatterns = new[]
            {
                "%3ajavascript:", "%3adata:", "%3avbscript:",
                "%3afile:", "%3aftp:", "%3ahttp:", "%3ahttps:"
            };
            if (encodedSchemePatterns.Any(pattern => lowerUrl.Contains(pattern)))
                return null;

            return url;
        }
        catch
        {
            return null;
        }
    }
}
