namespace HR.Infrastructure.Email;

/// <summary>
/// Defence-in-depth recipient filter for the live Postmark senders.
///
/// RFC 2606 / RFC 6761 permanently reserve certain TLDs (<c>.test</c>, <c>.example</c>,
/// <c>.invalid</c>, <c>.localhost</c>) and the second-level names <c>example.com/net/org</c> — mail
/// to any of them can never be delivered. This application's own dev / Playwright-E2E seed data uses
/// <c>acme.example</c> and <c>betacorp.example</c> personas. If one of those addresses ever reaches a
/// real Postmark sender — a live server token left configured in a non-production environment, seed
/// data provisioned somewhere it shouldn't be, a stray test address in real data — the send is a
/// guaranteed hard bounce, and hard bounces measurably degrade the shared sending domain's
/// reputation. This guard drops such a send before it leaves the process.
///
/// It is a safety net, not the primary control: the real fix for "don't send from non-prod" is not
/// registering the Postmark senders there at all (see <c>InfrastructureModule.AddEmailSender</c>).
/// </summary>
internal static class PostmarkRecipientGuard
{
    private static readonly string[] BlockedTlds =
        [".test", ".example", ".invalid", ".localhost"];

    private static readonly string[] BlockedExactDomains =
        ["example.com", "example.net", "example.org"];

    /// <summary>
    /// True when <paramref name="toEmail"/> is a syntactically-usable address whose domain can never
    /// receive mail. A null/blank/malformed address returns false — the caller's own
    /// missing-recipient handling owns that case.
    /// </summary>
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
