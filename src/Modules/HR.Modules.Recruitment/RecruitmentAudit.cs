using HR.SharedKernel;

namespace HR.Modules.Recruitment;

internal sealed record VacancyAuditSnapshot(
    string? AdvertTitle,
    string? AdvertDescription,
    Guid HiringManagerId,
    Domain.VacancyStatus Status);

// EffectiveTitle is resolved by the handler (vacancy.AdvertTitle ?? linked Position Profile's title)
// purely for a readable audit Summary line — it is not part of the Before/After snapshot itself,
// which records the vacancy's own raw field values only. Resolving it requires a cross-module read
// via IPositionProfileReader, which the handler performs, not this record.
internal sealed record VacancyUpdatedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    VacancyAuditSnapshot Before,
    VacancyAuditSnapshot After,
    string EffectiveTitle,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "vacancy.updated";
    string IAuditEvent.EntityType => "Vacancy";
    Guid IAuditEvent.EntityId => VacancyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Vacancy '{EffectiveTitle}' updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record VacancyClosedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    string EffectiveTitle,
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
    string? IAuditEvent.Summary => $"Vacancy '{EffectiveTitle}' closed";
    object? IAuditEvent.Before => new { Status = PreviousStatus };
    object? IAuditEvent.After => new { Status = Domain.VacancyStatus.Closed, ClosedAt };
    object? IAuditEvent.Metadata => null;
}

internal sealed record VacancyPositionProfileAssignedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    Guid? PreviousPositionProfileId,
    Guid PositionProfileId,
    string AssignmentMethod, // "auto_match" | "manual" | "update" | "authorised_correction"
    DateTimeOffset OccurredAt,
    // Populated only for the "authorised_correction" path (see UpdateVacancyHandler): who performed
    // the override and why. Null for the other assignment methods, which either have no authenticated
    // actor in scope (auto_match/manual) or don't require a reason (update).
    Guid? PerformedBy = null,
    string? CorrectionReason = null) : IAuditEvent
{
    string IAuditEvent.EventType => "vacancy.position_profile_assigned";
    string IAuditEvent.EntityType => "Vacancy";
    Guid IAuditEvent.EntityId => VacancyId;
    Guid? IAuditEvent.ActorUserId => PerformedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => AssignmentMethod == "authorised_correction"
        ? $"Vacancy position profile changed via authorised correction: {CorrectionReason}"
        : $"Vacancy assigned position profile ({AssignmentMethod})";
    object? IAuditEvent.Before => new { PositionProfileId = PreviousPositionProfileId };
    object? IAuditEvent.After => new { PositionProfileId };
    object? IAuditEvent.Metadata => new { AssignmentMethod, CorrectionReason };
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
