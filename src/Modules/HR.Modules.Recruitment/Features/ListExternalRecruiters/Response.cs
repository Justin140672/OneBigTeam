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
    int LinkedVacancyCount,
    DateTimeOffset CreatedAt);
