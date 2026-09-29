using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesRequest
{
    public Guid CompanyId { get; init; }
    public VacancyStatus? Status { get; init; }

    public Guid? PositionProfileId { get; init; }

    public Guid? DepartmentId { get; init; }

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

    public int? PageSize { get; init; }
}
