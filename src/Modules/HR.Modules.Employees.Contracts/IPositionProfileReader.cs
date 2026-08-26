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
    // Resolved display name for LocationId, or null when the profile has no location set (or the
    // location can no longer be found). Resolved by the implementation (owned by HR.Modules.Employees,
    // which also owns Location) rather than by callers, so consumers such as Recruitment's Vacancy
    // location-fallback resolution ("Location override ?? Position Profile location default", see
    // GetVacancyHandler/ListVacanciesHandler) never need a direct reference to the Employees module's
    // Location entity.
    string? LocationName,
    // Resolved display name for DepartmentId, or null when the profile has no department set (or the
    // department can no longer be found). Same rationale/resolution approach as LocationName — added
    // for the Recruitment Pipeline Summary Report (see IRecruitmentPipelineSummaryReader), which needs
    // a vacancy's department name without a direct reference to the Employees module's Department entity.
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
    /// <summary>
    /// Returns true when an active position profile with the given ID exists for the given company.
    /// Used to validate cross-module references (e.g. Vacancy.PositionProfileId) without a direct
    /// module-to-module reference or database foreign key.
    /// </summary>
    Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the IDs of every active position profile in the given company whose title matches
    /// <paramref name="title"/> (case-insensitive, trimmed) and, when <paramref name="departmentId"/>
    /// is supplied, belongs to that department. When <paramref name="departmentId"/> is null the
    /// search is company-wide (no department filter), which lets callers detect ambiguity across
    /// departments for records that have no department of their own.
    /// Used for position-profile matching/backfill (e.g. Vacancy -> PositionProfile) without a
    /// direct module-to-module reference or database foreign key.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId,
        Guid? departmentId,
        string title,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the DepartmentId owned by the given active position profile (or null if the profile
    /// has no department set). Callers must first confirm the profile exists for the company via
    /// <see cref="ExistsAsync"/> — this method also returns null when no matching profile is found,
    /// so on its own it cannot distinguish "no department" from "no such profile".
    /// Used so that callers deriving a record's department from a position profile (e.g. Vacancy
    /// creation) never have to trust a client-supplied department value.
    /// </summary>
    Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the given position profile's canonical role information (title, department,
    /// description), or null if no profile with that ID exists for the company. Active and inactive
    /// profiles both resolve (see <see cref="PositionProfileSummary"/>) so that read-only displays of
    /// existing records (e.g. Vacancy details) still show the linked profile's information even after
    /// it has been deactivated.
    /// </summary>
    Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// Batch form of <see cref="GetSummaryAsync"/> — avoids N+1 queries when resolving position
    /// profile summaries for a list of records (e.g. ListVacancies). Profiles that don't exist for the
    /// company are simply omitted from the result.
    /// </summary>
    Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the IDs of every position profile in the given company that belongs to the given
    /// department (active and inactive alike, so a department filter over historical records such as
    /// Vacancy search still matches profiles that have since been deactivated). Used so that callers
    /// needing to filter another module's records by department (e.g. ListVacancies) can resolve the
    /// matching position profile IDs first, without a direct module reference or database join.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(
        Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the given position profile's employment defaults (salary range/type, working pattern,
    /// probation override, default leave policy, location), or null if no profile with that ID exists
    /// for the company. See <see cref="PositionProfileEmploymentDefaults"/>.
    /// </summary>
    Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(
        Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the IDs of every active position profile in the given company. Used by IAM-03
    /// (position-based default role administration, owned by HR.Modules.Identity) to list the
    /// full set of position profiles an administrator can configure default roles for, without a
    /// direct module-to-module reference or database join.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the IDs of every position profile in the given company, active or inactive. Used by
    /// IAM-03's Identity-side reconciliation pass (see IdentityModule.ReconcilePositionRoleAssignmentsAsync)
    /// to backfill Identity's Position table for profiles that existed before position/role bridging
    /// was wired up — unlike <see cref="GetAllActiveIdsAsync"/>, inactive profiles must still be
    /// included so a subsequently-reactivated profile already has a matching Position row.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
