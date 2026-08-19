namespace HR.Modules.Companies.Contracts;

public interface ICompanyContactValidationReader
{
    Task<CompanyContactValidationRules> GetContactValidationRulesAsync(Guid companyId, CancellationToken cancellationToken);
}
