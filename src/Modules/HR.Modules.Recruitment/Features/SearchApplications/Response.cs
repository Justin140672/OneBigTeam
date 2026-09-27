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
    DateTimeOffset AppliedAt,
    // Internal recruitment Ticket 6: resolved stage name / withdrawal flag so a candidate's
    // application history can be rendered without further lookups.
    string? CurrentStageName = null,
    bool IsWithdrawn = false,
    // True only when Source == Internal. EmployeeId is populated only for internal applications —
    // never for external candidates, including those later hired.
    bool IsInternal = false,
    Guid? EmployeeId = null);
