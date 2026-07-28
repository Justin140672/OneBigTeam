namespace HR.Modules.Recruitment.Features.ListExternalRecruiters;

internal sealed record ListExternalRecruitersResponse(
    IReadOnlyList<ExternalRecruiterListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record ExternalRecruiterListItem(
    Guid Id,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    bool IsActive,
    // All-time count of vacancy links (active + historical/inactive assignments) — see
    // ListExternalRecruitersHandler for the rationale of counting all rather than active-only.
    int LinkedVacancyCount,
    DateTimeOffset CreatedAt);
