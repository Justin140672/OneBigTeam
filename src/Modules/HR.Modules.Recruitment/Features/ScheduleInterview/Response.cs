using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed record ScheduleInterviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid ApplicationId,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location,
    InterviewOutcome Outcome,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
