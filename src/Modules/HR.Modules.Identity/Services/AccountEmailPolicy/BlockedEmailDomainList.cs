using System.Collections.Frozen;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

/// <summary>
/// Ticket 9: loads and validates the account-creation email-domain denylist — the version-controlled
/// <c>blocked-email-domains.txt</c> file embedded in this assembly, plus any operator-supplied
/// <see cref="AccountEmailDomainPolicyOptions.AdditionalBlockedDomains"/>. The single place the
/// provider list is read from; nothing else in the solution may keep its own copy.
/// </summary>
internal static class BlockedEmailDomainList
{
    internal const string EmbeddedResourceName = "HR.Modules.Identity.AccountEmailPolicy.blocked-email-domains.txt";

    public static IReadOnlyList<string> ReadEmbeddedEntries()
    {
        using var stream = typeof(BlockedEmailDomainList).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Ticket 9: the account-creation email-domain denylist resource '{EmbeddedResourceName}' could not be loaded.");

        using var reader = new StreamReader(stream);
        return ParseEntries(reader.ReadToEnd());
    }

    internal static IReadOnlyList<string> ParseEntries(string content) =>
        content
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    public static (FrozenSet<string> Domains, IReadOnlyList<string> Errors) Build(
        IEnumerable<string> embeddedEntries,
        IEnumerable<string>? additionalEntries)
    {
        var errors = new List<string>();
        var domains = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? entry, string source)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                errors.Add($"{source} contains a blank entry.");
                return;
            }

            if (entry.Contains('@'))
            {
                errors.Add($"{source} entry '{entry}' must be a domain, not an email address.");
                return;
            }

            if (!AccountEmailDomain.TryNormalizeDomain(entry, out var normalized))
            {
                errors.Add($"{source} entry '{entry}' is not a valid domain.");
                return;
            }

            domains.Add(normalized);
        }

        foreach (var entry in embeddedEntries)
            Add(entry, "The embedded blocked-email-domains list");

        foreach (var entry in additionalEntries ?? [])
            Add(entry, $"Configuration '{AccountEmailDomainPolicyOptions.SectionName}:{nameof(AccountEmailDomainPolicyOptions.AdditionalBlockedDomains)}'");

        if (domains.Count == 0)
            errors.Add("The account-creation email-domain denylist is empty; refusing to allow every email domain.");

        return (domains.ToFrozenSet(StringComparer.Ordinal), errors);
    }
}
