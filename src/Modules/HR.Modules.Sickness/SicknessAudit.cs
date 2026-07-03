using HR.SharedKernel;

namespace HR.Modules.Sickness;

internal sealed record SicknessEvidenceRequestedAuditEvent(
    Guid EvidenceRequestId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.evidence_requested";
    string IAuditEvent.EntityType => "SicknessEvidenceRequest";
    Guid IAuditEvent.EntityId => EvidenceRequestId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Fit note evidence requested";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, DueDate };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessEvidenceFulfilledAuditEvent(
    Guid EvidenceRequestId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    DateTimeOffset FulfilledAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.evidence_fulfilled";
    string IAuditEvent.EntityType => "SicknessEvidenceRequest";
    Guid IAuditEvent.EntityId => EvidenceRequestId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Fit note evidence fulfilled";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, FulfilledAt };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.updated";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record updated";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessClosedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.closed";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record closed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record ReturnToWorkReviewRequiredAuditEvent(
    Guid ReviewId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.return_to_work_review_required";
    string IAuditEvent.EntityType => "ReturnToWorkReview";
    Guid IAuditEvent.EntityId => ReviewId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Return-to-work review required";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, DueDate };
    object? IAuditEvent.Metadata => null;
}

internal sealed record ReturnToWorkReviewCompletedAuditEvent(
    Guid ReviewId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ReviewedBy,
    string? Notes,
    DateTimeOffset CompletedAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.return_to_work_review_completed";
    string IAuditEvent.EntityType => "ReturnToWorkReview";
    Guid IAuditEvent.EntityId => ReviewId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ReviewedBy;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Return-to-work review completed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, CompletedAt, Notes };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessRecordedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.recorded";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}
