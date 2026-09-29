namespace HR.SharedKernel.Http;

/// <summary>
/// Validates and normalizes internal return URLs to prevent open-redirect vulnerabilities.
/// Accepts only application-relative paths and rejects scheme-relative URIs, absolute URLs,
/// and other potentially malicious patterns.
/// </summary>
public static class ReturnUrlValidator
{
    /// <summary>
    /// Validates an internal return URL and returns it if safe, null otherwise.
    /// Only application-relative paths (starting with exactly one '/') are accepted.
    /// </summary>
    /// <param name="returnUrl">The URL to validate, typically from a query parameter.</param>
    /// <returns>The validated URL if valid; null if invalid, empty, or dangerous.</returns>
    /// <summary>
    /// Like <see cref="ValidateInternalPath"/>, but also accepts base-relative paths without a leading
    /// '/' (as produced by NavigationManager.ToBaseRelativePath, which the kanban board and
    /// applications tab use when building returnUrl) by prefixing '/' before validating.
    /// Absolute URLs still fail because the '://' check runs on the prefixed value.
    /// </summary>
    public static string? ValidateInternalPathAllowingBaseRelative(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        var trimmed = returnUrl.Trim();
        return ValidateInternalPath(trimmed.StartsWith('/') ? trimmed : "/" + trimmed);
    }

    public static string? ValidateInternalPath(string? returnUrl)
    {
        // Empty, null, or whitespace-only values are invalid
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

            // Application-relative path is safe to use as a navigation target
            return url;
        }
        catch
        {
            // If any parsing or validation step throws, return null (invalid/unsafe URL)
            return null;
        }
    }
}
