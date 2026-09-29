namespace HR.Infrastructure.Email;

internal static class PostmarkRecipientGuard
{
    private static readonly string[] BlockedTlds =
        [".test", ".example", ".invalid", ".localhost"];

    private static readonly string[] BlockedExactDomains =
        ["example.com", "example.net", "example.org"];

    public static bool IsUndeliverable(string? toEmail)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return false;

        var at = toEmail.LastIndexOf('@');
        if (at <= 0 || at == toEmail.Length - 1)
            return false;

        var domain = toEmail[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length == 0)
            return false;

        foreach (var blocked in BlockedExactDomains)
            if (domain == blocked)
                return true;

        foreach (var tld in BlockedTlds)
            if (domain == tld[1..] || domain.EndsWith(tld, StringComparison.Ordinal))
                return true;

        return false;
    }
}
