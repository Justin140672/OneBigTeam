namespace HR.Modules.Companies.Contracts;

public sealed record CompanyWorkEmailSettings(
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    IReadOnlyList<string> AdditionalDomains,
    WorkEmailNamingConvention NamingConvention)
{
    public static CompanyWorkEmailSettings Default { get; } = new(
        true, null, [], WorkEmailNamingConvention.FirstNameDotLastName);

    public IReadOnlyList<string> AllDomains =>
        PrimaryDomain is null ? [] : [PrimaryDomain, .. AdditionalDomains];
}

public interface ICompanyWorkEmailSettingsReader
{
    Task<CompanyWorkEmailSettings> GetWorkEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
