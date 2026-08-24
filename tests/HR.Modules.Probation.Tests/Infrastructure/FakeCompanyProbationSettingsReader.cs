using HR.Modules.Companies.Contracts;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeCompanyProbationSettingsReader(
    IReadOnlyList<int>? checkpointDays = null,
    int probationMonths = 3) : ICompanyProbationSettingsReader
{
    public Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(probationMonths);

    public Task<IReadOnlyList<int>> GetCheckpointDaysAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(checkpointDays ?? CompanyProbationSettings.DefaultCheckpointDays);
}
