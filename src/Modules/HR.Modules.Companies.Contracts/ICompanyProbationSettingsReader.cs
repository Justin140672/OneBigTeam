namespace HR.Modules.Companies.Contracts;

public interface ICompanyProbationSettingsReader
{
    Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> GetCheckpointDaysAsync(Guid companyId, CancellationToken cancellationToken);
}
