namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

internal sealed record GetInterviewsRequiringActionMetricResponse(
    int Count,
    IReadOnlyList<InterviewRequiringActionItem> Items);

internal sealed record InterviewRequiringActionItem(
    Guid InterviewId,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid VacancyId,
    string VacancyTitle,
    DateTimeOffset ScheduledAt,
    string? Location);
