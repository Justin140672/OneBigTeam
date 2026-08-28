namespace HR.Modules.Recruitment.Features.SearchApplications;

internal sealed record SearchApplicationsResponse(
    IReadOnlyList<ApplicationSearchItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record ApplicationSearchItem(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    Guid CurrentStageId,
    DateTimeOffset AppliedAt);
