using HR.Modules.Employees.Contracts;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakePositionProfileReader(
    bool exists = true,
    Guid? matchingCompanyId = null,
    Guid? matchingPositionProfileId = null,
    IReadOnlyDictionary<Guid, PositionProfileSummary>? summaries = null,
    IReadOnlyList<Guid>? allActiveIds = null,
    IReadOnlyList<Guid>? allIds = null) : IPositionProfileReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        if (matchingCompanyId is null && matchingPositionProfileId is null)
            return Task.FromResult(exists);

        var matches = companyId == matchingCompanyId && positionProfileId == matchingPositionProfileId;
        return Task.FromResult(exists && matches);
    }

    public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId, Guid? departmentId, string title, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(summaries is not null && summaries.TryGetValue(positionProfileId, out var summary) ? summary : null);

    public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionProfileSummary> result = summaries is null
            ? []
            : positionProfileIds.Where(summaries.ContainsKey).Select(id => summaries[id]).ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult<PositionProfileEmploymentDefaults?>(null);

    public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(allActiveIds ?? []);

    public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(allIds ?? []);
}
