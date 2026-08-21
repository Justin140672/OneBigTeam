using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesRequest
{
    public Guid CompanyId { get; init; }
    public VacancyStatus? Status { get; init; }

    // Direct filter — matches Vacancy.PositionProfileId exactly.
    public Guid? PositionProfileId { get; init; }

    // Indirect filter — Vacancy has no department column of its own (department comes exclusively
    // from the linked Position Profile), so the handler first resolves the set of Position Profile IDs
    // belonging to this department (via IPositionProfileReader.GetIdsByDepartmentAsync) and then
    // filters vacancies whose PositionProfileId is in that set.
    public Guid? DepartmentId { get; init; }

    // "Show Active"/"Show Inactive" toggle — matches ListDepartmentsRequest's own ShowInactive
    // convention. Pushed to SQL (a real Vacancy.Status column comparison), unlike Search below.
    public bool ExcludeClosed { get; init; }

    // Matched against EffectiveTitle (AdvertTitle, falling back to the linked Position Profile's
    // title) and PositionProfileTitle. Applied in-memory after the read layer has already resolved
    // both from the cross-module IPositionProfileReader lookup below, rather than pushed to SQL —
    // EffectiveTitle isn't a real column to filter on, and this module deliberately never queries
    // the Position Profile table directly (see DepartmentId's own remarks on the narrow-contract
    // convention), so a genuine SQL-level search would need a new IPositionProfileReader method.
    // Vacancy counts per company are small enough that in-memory filtering here is a reasonable,
    // low-risk way to still make search a server-side concern from the caller's point of view —
    // VacancyList.razor no longer needs its own client-side filtering property, matching every
    // other SearchPageBase list page (see VacancyList.razor's own remarks on the bug this fixes).
    public string? Search { get; init; }

    // Optional/backward-compatible — omitted by every existing caller (VacancyList.razor's admin
    // grid needs the full company set for its own client-side paging), so their behavior is
    // unchanged. Added for callers that only need a bounded, type-to-search dropdown (see
    // RecruitmentDashboard.razor's vacancy picker) — the "vacancy counts per company are small"
    // assumption behind Search's own in-memory filtering above stopped holding once a long-lived
    // shared company accumulates vacancies across hundreds of E2E test runs that each create a
    // fresh one and never close/clean it up, which is exactly the same shape of real,
    // user-observed slow-dropdown regression already found and fixed for Position Profiles.
    public int? PageSize { get; init; }
}
