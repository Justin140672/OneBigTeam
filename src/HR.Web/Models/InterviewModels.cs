namespace HR.Web.Models;

// ── TODAY COUNT ───────────────────────────────────────────────────────────────

public record GetInterviewsTodayCountResponse(int Count);

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListInterviewsForVacancyResponse(List<InterviewListItemModel> Items);

public record InterviewListItemModel(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location,
    string Outcome,
    string? Notes);

// ── SCHEDULE ──────────────────────────────────────────────────────────────────

public record ScheduleInterviewRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location);

public record ScheduleInterviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid ApplicationId,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location,
    string Outcome,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── RECORD OUTCOME ────────────────────────────────────────────────────────────

public record RecordInterviewOutcomeRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    Guid InterviewId,
    string Outcome,
    string? Notes);

public record RecordInterviewOutcomeResponse(
    Guid Id,
    Guid CompanyId,
    Guid ApplicationId,
    Guid InterviewerEmployeeId,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes,
    string? Location,
    string Outcome,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
