using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

internal enum AccountEmailDomainVerdict
{
    Allowed = 0,

    Blocked = 1,

    Malformed = 2,
}

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

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Ticket 9: the account-creation email-domain denylist is invalid: " + string.Join(" ", errors));

        _blockedDomains = domains;
    }

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
