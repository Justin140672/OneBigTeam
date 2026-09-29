namespace HR.Modules.Employees.Contracts;

/// <summary>
/// A position profile's canonical role information, as owned by HR.Modules.Employees. Used by
/// consumers (e.g. Recruitment's Vacancy details) that need to display the profile's own title,
/// department and description as the authoritative source, distinct from any consumer-local
/// override fields. Deliberately not filtered by <see cref="IsActive"/> at the query level — unlike
/// <see cref="IPositionProfileReader.ExistsAsync"/>/<see cref="IPositionProfileReader.GetDepartmentIdAsync"/>,
/// which are used for create-time validation and must only match active profiles, a read-time summary
/// should still resolve for a profile that has since been deactivated so historical records remain
/// displayable; <see cref="IsActive"/> is surfaced so the caller can indicate that in the UI.
/// </summary>
public sealed record PositionProfileSummary(
    Guid Id,
    string Title,
    Guid? DepartmentId,
    string? Description,
    bool IsActive,
    Guid? LocationId,
    string? LocationName,
    string? DepartmentName = null);

/// <summary>
/// A position profile's employment defaults, as owned by HR.Modules.Employees. Surfaced as read-only
/// informational context (e.g. by Recruitment's OfferCandidate response) so HR can see what the role's
/// defined compensation/terms are while deciding to make an offer — this is not a negotiable-offer-terms
/// input, just a projection of the Position Profile's own defaults. SalaryType is exposed as its string
/// name (e.g. "Annual") rather than a shared enum, since HR.Modules.Employees.Domain.SalaryType is
/// internal to that module and must not be referenced directly from another module or from Infrastructure.
/// </summary>
public sealed record PositionProfileEmploymentDefaults(
    Guid PositionProfileId,
    string Title,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    int? ProbationMonthsOverride,
    Guid? DefaultLeavePolicyId,
    Guid? LocationId,
    string? LocationName);

public interface IPositionProfileReader
{
    Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId,
        Guid? departmentId,
        string title,
        CancellationToken cancellationToken);

    Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(
        Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(
        Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
