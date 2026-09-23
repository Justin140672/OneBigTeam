using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

internal enum AccountEmailDomainVerdict
{
    Allowed = 0,

    /// <summary>The domain (or a parent domain) is on the public/disposable denylist.</summary>
    Blocked = 1,

    /// <summary>The address could not be parsed/normalised, so its domain can't be evaluated.</summary>
    Malformed = 2,
}

/// <param name="Domain">The normalised domain (null when malformed). Safe to log/audit — never the full address.</param>
/// <param name="MatchedBlockedDomain">The denylist entry that matched (differs from Domain for subdomain matches).</param>
internal sealed record AccountEmailDomainEvaluation(
    AccountEmailDomainVerdict Verdict,
    string? Domain,
    string? MatchedBlockedDomain)
{
    public bool IsAllowed => Verdict == AccountEmailDomainVerdict.Allowed;
}

/// <summary>
/// Ticket 9: the single shared rule deciding whether an email address may be used to create a new
/// One Big Team user account. Pure and synchronous — no network lookups, no external
/// domain-classification services. Used by <see cref="AccountCreationEmailGuard"/> (every
/// account-creation handler) and by <see cref="SupabaseAuthGateway"/> as a last line of defence.
/// </summary>
internal interface IAccountEmailDomainPolicy
{
    AccountEmailDomainEvaluation Evaluate(string? email);
}

internal sealed class AccountEmailDomainPolicy : IAccountEmailDomainPolicy
{
    private static readonly Lazy<AccountEmailDomainPolicy> DefaultInstance =
        new(() => new AccountEmailDomainPolicy(additionalBlockedDomains: null));

    private readonly FrozenSet<string> _blockedDomains;

    public AccountEmailDomainPolicy(IOptions<AccountEmailDomainPolicyOptions> options)
        : this(options.Value.AdditionalBlockedDomains)
    {
    }

    internal AccountEmailDomainPolicy(IEnumerable<string>? additionalBlockedDomains)
    {
        var (domains, errors) = BlockedEmailDomainList.Build(
            BlockedEmailDomainList.ReadEmbeddedEntries(), additionalBlockedDomains);

        // Fail closed: an invalid or empty denylist must never degrade into "allow everything".
        // ValidateOnStart normally stops the host before this is reached; this guards any other
        // construction path.
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Ticket 9: the account-creation email-domain denylist is invalid: " + string.Join(" ", errors));

        _blockedDomains = domains;
    }

    /// <summary>
    /// The baseline policy built from the embedded denylist only — used where no DI-configured
    /// instance is supplied (e.g. directly-constructed gateways), never a permissive fallback.
    /// </summary>
    public static AccountEmailDomainPolicy Default => DefaultInstance.Value;

    public IReadOnlyCollection<string> BlockedDomains => _blockedDomains;

    public AccountEmailDomainEvaluation Evaluate(string? email)
    {
        if (!AccountEmailDomain.TryExtractDomain(email, out var domain))
            return new AccountEmailDomainEvaluation(AccountEmailDomainVerdict.Malformed, Domain: null, MatchedBlockedDomain: null);

        var matched = FindBlockedEntry(domain);
        return matched is null
            ? new AccountEmailDomainEvaluation(AccountEmailDomainVerdict.Allowed, domain, MatchedBlockedDomain: null)
            : new AccountEmailDomainEvaluation(AccountEmailDomainVerdict.Blocked, domain, matched);
    }

    // Exact match or any subdomain of a blocked entry — compared label-by-label (walking up the
    // parent domains), never by raw string suffix, so "olive.com" is not caught by "live.com" and
    // "acme.com" is not caught by "me.com".
    private string? FindBlockedEntry(string domain)
    {
        var candidate = domain;
        while (true)
        {
            if (_blockedDomains.Contains(candidate))
                return candidate;

            var dot = candidate.IndexOf('.');
            if (dot < 0)
                return null;

            candidate = candidate[(dot + 1)..];
        }
    }
}
