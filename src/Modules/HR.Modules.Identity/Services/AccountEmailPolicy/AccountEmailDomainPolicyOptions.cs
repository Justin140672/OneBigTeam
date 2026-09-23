using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

/// <summary>
/// Ticket 9: operator configuration for the account-creation email-domain policy. The baseline
/// denylist is the version-controlled embedded <c>blocked-email-domains.txt</c>; this section only
/// extends it (e.g. a newly observed disposable provider) without a code change. It can never
/// remove a baseline entry, so a configuration mistake cannot open the policy up.
/// </summary>
internal sealed class AccountEmailDomainPolicyOptions
{
    public const string SectionName = "Identity:AccountEmailDomainPolicy";

    public List<string> AdditionalBlockedDomains { get; set; } = [];
}

/// <summary>
/// Ticket 9: startup validation (registered with <c>ValidateOnStart</c>) — the API refuses to start
/// when the embedded denylist is missing/empty or any embedded/configured entry is malformed, so a
/// broken configuration is visible immediately instead of silently allowing every address.
/// </summary>
internal sealed class AccountEmailDomainPolicyOptionsValidator : IValidateOptions<AccountEmailDomainPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountEmailDomainPolicyOptions options)
    {
        IReadOnlyList<string> embedded;
        try
        {
            embedded = BlockedEmailDomainList.ReadEmbeddedEntries();
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        var (_, errors) = BlockedEmailDomainList.Build(embedded, options.AdditionalBlockedDomains);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"Ticket 9 account email-domain policy: {e}"));
    }
}
