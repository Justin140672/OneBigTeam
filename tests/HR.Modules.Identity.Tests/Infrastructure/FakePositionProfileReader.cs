using HR.Modules.Employees.Contracts;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Fake for IPositionProfileReader, mirroring tests/HR.Modules.Recruitment.Tests/Infrastructure/FakePositionProfileReader.cs.
/// IAM-03's Identity-side consumers (PositionSync, SetPositionRoleDefaultsHandler,
/// ListPositionRoleDefaultsHandler) only use ExistsAsync, GetSummaryAsync, GetSummariesAsync,
/// GetAllActiveIdsAsync and GetAllIdsAsync — the other IPositionProfileReader members are
/// implemented here only to satisfy the interface and are not exercised by Identity's tests.
/// </summary>
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

    /// <summary>Defaults to null (no summary / profile can't be resolved for the company). Pass <paramref name="summaries"/> (keyed by position profile ID) to simulate a resolvable profile.</summary>
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

    /// <summary>Defaults to empty. Pass <paramref name="allActiveIds"/> to simulate the company's active position profile roster.</summary>
    public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(allActiveIds ?? []);

    /// <summary>Defaults to empty. Pass <paramref name="allIds"/> to simulate the company's full (active + inactive) position profile roster.</summary>
    public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(allIds ?? []);
}
