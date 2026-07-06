using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListInterviewsForVacancy;

internal sealed record ListInterviewsForVacancyResponse(IReadOnlyList<InterviewListItem> Items);

internal sealed record InterviewListItem(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location,
    InterviewOutcome Outcome,
    string? Notes);
