namespace HR.Modules.Companies.Contracts;

public interface ICompanyProbationSettingsReader
{
    Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken);
}
