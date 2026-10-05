namespace HR.Modules.Companies.Contracts;

public sealed record CompanyWorkEmailSettings(
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention)
{
    public static CompanyWorkEmailSettings Default { get; } = new(
        true, null, WorkEmailNamingConvention.FirstNameDotLastName);
}

public interface ICompanyWorkEmailSettingsReader
{
    Task<CompanyWorkEmailSettings> GetWorkEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
