using HR.SharedKernel;

namespace HR.Modules.Recruitment;

internal sealed record VacancyAuditSnapshot(
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    Guid HiringManagerId,
    Domain.VacancyStatus Status);

internal sealed record VacancyUpdatedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    VacancyAuditSnapshot Before,
    VacancyAuditSnapshot After,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "vacancy.updated";
    string IAuditEvent.EntityType => "Vacancy";
    Guid IAuditEvent.EntityId => VacancyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Vacancy '{After.Title}' updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record VacancyClosedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    string Title,
    Domain.VacancyStatus PreviousStatus,
    DateOnly ClosedAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "vacancy.closed";
    string IAuditEvent.EntityType => "Vacancy";
    Guid IAuditEvent.EntityId => VacancyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Vacancy '{Title}' closed";
    object? IAuditEvent.Before => new { Status = PreviousStatus };
    object? IAuditEvent.After => new { Status = Domain.VacancyStatus.Closed, ClosedAt };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CandidateAuditSnapshot(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl);

internal sealed record CandidateUpdatedAuditEvent(
    Guid CompanyId,
    Guid CandidateId,
    CandidateAuditSnapshot Before,
    CandidateAuditSnapshot After,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "candidate.updated";
    string IAuditEvent.EntityType => "Candidate";
    Guid IAuditEvent.EntityId => CandidateId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Candidate '{After.FirstName} {After.LastName}' updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record InterviewOutcomeRecordedAuditEvent(
    Guid CompanyId,
    Guid InterviewId,
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Domain.InterviewOutcome Outcome,
    string? Notes,
    Guid RecordedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "interview.outcome_recorded";
    string IAuditEvent.EntityType => "Interview";
    Guid IAuditEvent.EntityId => InterviewId;
    Guid? IAuditEvent.ActorUserId => RecordedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Interview outcome recorded as '{Outcome}'";
    object? IAuditEvent.Before => new { Outcome = Domain.InterviewOutcome.Pending };
    object? IAuditEvent.After => new { Outcome, Notes };
    object? IAuditEvent.Metadata => new { ApplicationId, VacancyId, CandidateId };
}

internal sealed record CandidateHiredAuditEvent(
    Guid CompanyId,
    Guid CandidateId,
    Guid ApplicationId,
    Guid VacancyId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "candidate.hired";
    string IAuditEvent.EntityType => "Candidate";
    Guid IAuditEvent.EntityId => CandidateId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Candidate hired and provisioned as employee";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ApplicationId, VacancyId, EmployeeId };
    object? IAuditEvent.Metadata => null;
}
