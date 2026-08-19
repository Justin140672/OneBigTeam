using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Fake for IPositionProfileReader. Defaults to "exists" for every lookup; pass explicit
/// companyId/positionProfileId to only match a specific pair (e.g. to simulate a position profile
/// that belongs to a different company, or one that does not exist at all).
/// </summary>
internal sealed class FakePositionProfileReader(
    bool exists = true,
    Guid? matchingCompanyId = null,
    Guid? matchingPositionProfileId = null,
    IReadOnlyList<Guid>? activeMatches = null,
    Guid? departmentId = null,
    IReadOnlyDictionary<Guid, PositionProfileSummary>? summaries = null,
    IReadOnlyList<Guid>? idsByDepartment = null,
    IReadOnlyDictionary<Guid, PositionProfileEmploymentDefaults>? employmentDefaults = null) : IPositionProfileReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
    {
        if (matchingCompanyId is null && matchingPositionProfileId is null)
            return Task.FromResult(exists);

        var matches = companyId == matchingCompanyId && positionProfileId == matchingPositionProfileId;
        return Task.FromResult(exists && matches);
    }

    /// <summary>
    /// Defaults to an empty result (no matches / unmatched). Pass <paramref name="activeMatches"/> in
    /// the constructor to simulate a single exact match (one ID -> Matched) or an ambiguous match
    /// (two-or-more IDs -> Ambiguous) for VacancyPositionProfileMatcher tests.
    /// </summary>
    public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId,
        Guid? departmentId,
        string title,
        CancellationToken cancellationToken) =>
        Task.FromResult(activeMatches ?? []);

    /// <summary>
    /// Defaults to null (no department). Pass <paramref name="departmentId"/> in the constructor to
    /// simulate a position profile that belongs to a specific department, e.g. for
    /// CreateVacancyHandler tests verifying department is derived from the profile, not the client.
    /// </summary>
    public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(departmentId);

    /// <summary>
    /// Defaults to null (no summary / profile can't be resolved). Pass <paramref name="summaries"/>
    /// in the constructor (keyed by position profile ID) to simulate a resolvable — active or
    /// inactive — position profile for GetVacancy/ListVacancies tests.
    /// </summary>
    public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(summaries is not null && summaries.TryGetValue(positionProfileId, out var summary) ? summary : null);

    /// <summary>Batch form of <see cref="GetSummaryAsync"/> — returns only the IDs present in <paramref name="summaries"/>.</summary>
    public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionProfileSummary> result = summaries is null
            ? []
            : positionProfileIds.Where(summaries.ContainsKey).Select(id => summaries[id]).ToList();

        return Task.FromResult(result);
    }

    /// <summary>Defaults to empty. Pass <paramref name="idsByDepartment"/> to simulate matches for ListVacancies department-filter tests.</summary>
    public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(idsByDepartment ?? []);

    /// <summary>Defaults to null (no employment defaults). Pass <paramref name="employmentDefaults"/> (keyed by position profile ID) to simulate a resolvable profile for OfferCandidate tests.</summary>
    public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(employmentDefaults is not null && employmentDefaults.TryGetValue(positionProfileId, out var defaults) ? defaults : null);
}
