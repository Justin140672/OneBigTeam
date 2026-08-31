namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

/// <summary>
/// DSH-04 authoritative "Interviews requiring action" metric. <see cref="Count"/> always equals
/// <c>Items.Count</c>.
/// </summary>
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
