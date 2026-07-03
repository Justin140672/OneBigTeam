namespace HR.Infrastructure.Abstractions;

public interface ICompanyProbationSettingsReader
{
    Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken);
}
