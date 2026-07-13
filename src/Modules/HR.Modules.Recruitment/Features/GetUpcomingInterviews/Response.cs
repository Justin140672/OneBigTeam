namespace HR.Modules.Recruitment.Features.GetUpcomingInterviews;

internal sealed record GetUpcomingInterviewsResponse(IReadOnlyList<UpcomingInterviewItem> Items);

internal sealed record UpcomingInterviewItem(
    Guid InterviewId,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid VacancyId,
    string VacancyTitle,
    DateTimeOffset ScheduledAt,
    string? Location);
