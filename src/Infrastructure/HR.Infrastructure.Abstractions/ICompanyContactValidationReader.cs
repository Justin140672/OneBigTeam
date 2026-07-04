namespace HR.Infrastructure.Abstractions;

public interface ICompanyContactValidationReader
{
    Task<CompanyContactValidationRules> GetContactValidationRulesAsync(Guid companyId, CancellationToken cancellationToken);
}
