namespace HR.SharedKernel;

public interface ICompanyProbationSettingsReader
{
    Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken);
}
