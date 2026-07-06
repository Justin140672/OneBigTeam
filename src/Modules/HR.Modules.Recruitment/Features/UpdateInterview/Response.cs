using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.UpdateInterview;

internal sealed record UpdateInterviewResponse(
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
