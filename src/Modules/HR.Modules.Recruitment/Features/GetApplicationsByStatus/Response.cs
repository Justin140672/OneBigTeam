namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed record GetApplicationsByStatusResponse(IReadOnlyList<ApplicationByStatusItem> Items);

internal sealed record ApplicationByStatusItem(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    DateTimeOffset AppliedAt);
