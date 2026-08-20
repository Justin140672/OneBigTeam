using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Recruitment;

internal sealed record VacancyAuditSnapshot(
    string? AdvertTitle,
    string? AdvertDescription,
    Guid HiringManagerId,
    Domain.VacancyStatus Status,
    // Ticket #81: the assigned external recruitment agency (ExternalRecruiter.Id), folded into the
    // existing vacancy.updated audit event rather than a bespoke event — this is now a plain optional
    // field on Vacancy, not a separate assignment entity with its own audit trail.
    Guid? AssignedRecruiterId);

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

internal sealed record VacancyPublishedAuditEvent(
    Guid CompanyId,
    Guid VacancyId,
    string EffectiveTitle,
    Domain.VacancyStatus PreviousStatus,
    DateOnly OpenedAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "vacancy.published";
    string IAuditEvent.EntityType => "Vacancy";
    Guid IAuditEvent.EntityId => VacancyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Vacancy '{EffectiveTitle}' published";
    object? IAuditEvent.Before => new { Status = PreviousStatus };
    object? IAuditEvent.After => new { Status = Domain.VacancyStatus.Open, OpenedAt };
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

internal sealed record CandidateDeactivatedAuditEvent(
    Guid CompanyId,
    Guid CandidateId,
    string CandidateName,
    string Reason,
    Guid DeactivatedByUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "candidate.deactivated";
    string IAuditEvent.EntityType => "Candidate";
    Guid IAuditEvent.EntityId => CandidateId;
    Guid? IAuditEvent.ActorUserId => DeactivatedByUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Candidate '{CandidateName}' deactivated: {Reason}";
    object? IAuditEvent.Before => new { IsActive = true };
    object? IAuditEvent.After => new { IsActive = false, Reason };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CandidateReactivatedAuditEvent(
    Guid CompanyId,
    Guid CandidateId,
    string CandidateName,
    Guid ReactivatedByUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "candidate.reactivated";
    string IAuditEvent.EntityType => "Candidate";
    Guid IAuditEvent.EntityId => CandidateId;
    Guid? IAuditEvent.ActorUserId => ReactivatedByUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Candidate '{CandidateName}' reactivated";
    object? IAuditEvent.Before => new { IsActive = false };
    object? IAuditEvent.After => new { IsActive = true };
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

// Deliberately separate from ApplicationStageHistoryEntry (a domain-specific record surfaced on the
// applicant record via GetApplication.StageHistory): this is the cross-cutting "who changed
// business data" audit log entry, published for every successful stage change (named transition
// methods and the generic MoveToStage path alike) via RecruitmentStageChangeRecorder.
// Ticket #99: PreviousStageId/NewStageId are RecruitmentStage ids (Guid) rather than
// ApplicationStatus enum values; PreviousStageName/NewStageName are resolved by
// RecruitmentStageChangeRecorder for a readable Summary/audit payload.
internal sealed record ApplicationStageChangedAuditEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Guid PreviousStageId,
    string PreviousStageName,
    Guid NewStageId,
    string NewStageName,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "application.stage_changed";
    string IAuditEvent.EntityType => "Application";
    Guid IAuditEvent.EntityId => ApplicationId;
    Guid? IAuditEvent.ActorUserId => ChangedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Application moved from '{PreviousStageName}' to '{NewStageName}'";
    object? IAuditEvent.Before => new { StageId = PreviousStageId, Stage = PreviousStageName };
    object? IAuditEvent.After => new { StageId = NewStageId, Stage = NewStageName };
    object? IAuditEvent.Metadata => new { VacancyId, CandidateId };
}

internal sealed record ExternalRecruiterCreatedAuditEvent(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    string AgencyName,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "external_recruiter.created";
    string IAuditEvent.EntityType => "ExternalRecruiter";
    Guid IAuditEvent.EntityId => ExternalRecruiterId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"External recruiter '{AgencyName}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { AgencyName };
    object? IAuditEvent.Metadata => null;
}

internal sealed record ExternalRecruiterAuditSnapshot(
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes);

internal sealed record ExternalRecruiterUpdatedAuditEvent(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    ExternalRecruiterAuditSnapshot Before,
    ExternalRecruiterAuditSnapshot After,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "external_recruiter.updated";
    string IAuditEvent.EntityType => "ExternalRecruiter";
    Guid IAuditEvent.EntityId => ExternalRecruiterId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"External recruiter '{After.AgencyName}' updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record ExternalRecruiterActiveStatusChangedAuditEvent(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    string AgencyName,
    bool PreviousIsActive,
    bool NewIsActive,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "external_recruiter.active_status_changed";
    string IAuditEvent.EntityType => "ExternalRecruiter";
    Guid IAuditEvent.EntityId => ExternalRecruiterId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => NewIsActive
        ? $"External recruiter '{AgencyName}' reactivated"
        : $"External recruiter '{AgencyName}' deactivated";
    object? IAuditEvent.Before => new { IsActive = PreviousIsActive };
    object? IAuditEvent.After => new { IsActive = NewIsActive };
    object? IAuditEvent.Metadata => null;
}

// Ticket #78: published whenever an application's source/recruiter attribution is set (at creation
// today; also intended for any future "edit source" endpoint). SourceExternalRecruiterId references
// the ExternalRecruiter row directly and is preserved verbatim in the audit trail even if that
// recruiter's vacancy assignment is later removed.
internal sealed record ApplicationSourceSetAuditEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Domain.ApplicationSource Source,
    Guid? SourceExternalRecruiterId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "application.source_set";
    string IAuditEvent.EntityType => "Application";
    Guid IAuditEvent.EntityId => ApplicationId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Application source set to '{Source}'";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Source, SourceExternalRecruiterId };
    object? IAuditEvent.Metadata => new { VacancyId, CandidateId };
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

// Ticket #99: published whenever a candidate withdraws an application. Deliberately not folded into
// ApplicationStageChangedAuditEvent, since withdrawal never changes CurrentStageId (see
// Application.WithdrawnAt's remarks) — this is a distinct, additive fact about the application.
internal sealed record ApplicationWithdrawnAuditEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Guid StageIdAtWithdrawal,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "application.withdrawn";
    string IAuditEvent.EntityType => "Application";
    Guid IAuditEvent.EntityId => ApplicationId;
    Guid? IAuditEvent.ActorUserId => ChangedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Application withdrawn by candidate";
    object? IAuditEvent.Before => new { WithdrawnAt = (DateTimeOffset?)null };
    object? IAuditEvent.After => new { WithdrawnAt = OccurredAt, StageIdAtWithdrawal };
    object? IAuditEvent.Metadata => new { VacancyId, CandidateId };
}

// Ticket #97: audit events for the new per-company RecruitmentStage settings CRUD.
internal sealed record RecruitmentStageCreatedAuditEvent(
    Guid CompanyId,
    Guid RecruitmentStageId,
    string Name,
    int DisplayOrder,
    bool IsTerminal,
    Domain.RecruitmentStageTerminalOutcome TerminalOutcome,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "recruitment_stage.created";
    string IAuditEvent.EntityType => "RecruitmentStage";
    Guid IAuditEvent.EntityId => RecruitmentStageId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Recruitment stage '{Name}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Name, DisplayOrder, IsTerminal, TerminalOutcome };
    object? IAuditEvent.Metadata => null;
}

internal sealed record RecruitmentStageAuditSnapshot(
    string Name,
    bool IsTerminal,
    Domain.RecruitmentStageTerminalOutcome TerminalOutcome);

internal sealed record RecruitmentStageUpdatedAuditEvent(
    Guid CompanyId,
    Guid RecruitmentStageId,
    RecruitmentStageAuditSnapshot Before,
    RecruitmentStageAuditSnapshot After,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "recruitment_stage.updated";
    string IAuditEvent.EntityType => "RecruitmentStage";
    Guid IAuditEvent.EntityId => RecruitmentStageId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Recruitment stage '{After.Name}' updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record RecruitmentStagesReorderedAuditEvent(
    Guid CompanyId,
    IReadOnlyList<Guid> OrderedStageIds,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "recruitment_stage.reordered";
    string IAuditEvent.EntityType => "RecruitmentStage";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Recruitment stages reordered";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { OrderedStageIds };
    object? IAuditEvent.Metadata => null;
}

internal sealed record RecruitmentStageActiveStatusChangedAuditEvent(
    Guid CompanyId,
    Guid RecruitmentStageId,
    string Name,
    bool PreviousIsActive,
    bool NewIsActive,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "recruitment_stage.active_status_changed";
    string IAuditEvent.EntityType => "RecruitmentStage";
    Guid IAuditEvent.EntityId => RecruitmentStageId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => NewIsActive
        ? $"Recruitment stage '{Name}' reactivated"
        : $"Recruitment stage '{Name}' deactivated";
    object? IAuditEvent.Before => new { IsActive = PreviousIsActive };
    object? IAuditEvent.After => new { IsActive = NewIsActive };
    object? IAuditEvent.Metadata => null;
}
