using HR.SharedKernel;

namespace HR.Modules.Sickness;

internal sealed record SicknessEvidenceRequestedAuditEvent(
    Guid EvidenceRequestId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.evidence_requested";
    string IAuditEvent.EntityType => "SicknessEvidenceRequest";
    Guid IAuditEvent.EntityId => EvidenceRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Fit note evidence requested, due {DueDate:d MMM yyyy}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, DueDate };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessEvidenceFulfilledAuditEvent(
    Guid EvidenceRequestId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorId,
    DateTimeOffset FulfilledAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    // Ticket 15 (P1): deterministic EventId derived from the evidence-request id — exactly one
    // fulfilment event is ever meaningful per request, so a recovered/replayed dispatch (see
    // SicknessEvidenceUploadCompletionAction) can never create a duplicate audit row.
    Guid IAuditEvent.EventId => EvidenceRequestId;
    string IAuditEvent.EventType => "sickness.evidence_fulfilled";
    string IAuditEvent.EntityType => "SicknessEvidenceRequest";
    Guid IAuditEvent.EntityId => EvidenceRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorId;
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
    Guid? ActorEmployeeIdValue,
    Guid BeforeCategoryId,
    DateOnly BeforeStartDate,
    DateOnly? BeforeEndDate,
    decimal? BeforeTotalDays,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.updated";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record updated";
    object? IAuditEvent.Before => new { CategoryId = BeforeCategoryId, StartDate = BeforeStartDate, EndDate = BeforeEndDate, TotalDays = BeforeTotalDays };
    // SICK-06: deliberately excludes Notes — free-text health content never appears in audit payloads.
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessClosedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid? ActorEmployeeIdValue,
    Guid BeforeCategoryId,
    DateOnly BeforeStartDate,
    DateOnly? BeforeEndDate,
    decimal? BeforeTotalDays,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.closed";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record closed";
    object? IAuditEvent.Before => new { CategoryId = BeforeCategoryId, StartDate = BeforeStartDate, EndDate = BeforeEndDate, TotalDays = BeforeTotalDays };
    // SICK-06: deliberately excludes Notes — free-text health content never appears in audit payloads.
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record ReturnToWorkReviewRequiredAuditEvent(
    Guid ReviewId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid? ActorEmployeeIdValue,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.return_to_work_review_required";
    string IAuditEvent.EntityType => "ReturnToWorkReview";
    Guid IAuditEvent.EntityId => ReviewId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Return-to-work review required by {DueDate:d MMM yyyy}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, DueDate };
    object? IAuditEvent.Metadata => null;
}

/// <summary>
/// SICK-06: ActorEmployeeId is the reviewer (ReviewedBy — resolved server-side from ICurrentUser
/// in CompleteReturnToWorkReviewHandler), correctly distinct from EmployeeId (the subject being
/// reviewed). AdjustmentDetails and Notes are deliberately NOT carried in the After payload — both
/// are free-text fields that may contain sensitive health/medical information (see
/// SicknessResourceAuthorizer's equivalent restriction on the API response surface for these same
/// fields). Only safe, structured summary data (outcome, and booleans indicating whether
/// adjustments/notes were recorded) is included.
/// </summary>
internal sealed record ReturnToWorkReviewCompletedAuditEvent(
    Guid ReviewId,
    Guid SicknessRecordId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ReviewedBy,
    string Outcome,
    bool AdjustmentsRequired,
    bool HasAdjustmentDetails,
    bool HasNotes,
    DateTimeOffset CompletedAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.return_to_work_review_completed";
    string IAuditEvent.EntityType => "ReturnToWorkReview";
    Guid IAuditEvent.EntityId => ReviewId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ReviewedBy;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Return-to-work review completed with outcome {Outcome}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SicknessRecordId, CompletedAt, Outcome, AdjustmentsRequired, HasAdjustmentDetails, HasNotes };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessRecordReopenedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid ReviewId,
    Guid ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.record_reopened";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record reopened following a not-fit return-to-work review";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ReviewId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessRecordedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid? ActorEmployeeIdValue,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.recorded";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record created";
    object? IAuditEvent.Before => null;
    // SICK-06: deliberately excludes Notes — free-text health content never appears in audit payloads.
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessCategoryCreatedAuditEvent(
    Guid CompanyId,
    Guid CategoryId,
    Guid? ActorEmployeeIdValue,
    string Name,
    int DisplayOrder,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.category_created";
    string IAuditEvent.EntityType => "SicknessCategory";
    Guid IAuditEvent.EntityId => CategoryId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Sickness category '{Name}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Name, DisplayOrder, IsActive = true };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessCategoryUpdatedAuditEvent(
    Guid CompanyId,
    Guid CategoryId,
    Guid? ActorEmployeeIdValue,
    string BeforeName,
    int BeforeDisplayOrder,
    bool BeforeIsActive,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.category_updated";
    string IAuditEvent.EntityType => "SicknessCategory";
    Guid IAuditEvent.EntityId => CategoryId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Sickness category '{Name}' updated";
    object? IAuditEvent.Before => new { Name = BeforeName, DisplayOrder = BeforeDisplayOrder, IsActive = BeforeIsActive };
    object? IAuditEvent.After => new { Name, DisplayOrder, IsActive };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessCategoryDeactivatedAuditEvent(
    Guid CompanyId,
    Guid CategoryId,
    Guid? ActorEmployeeIdValue,
    string Name,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.category_deactivated";
    string IAuditEvent.EntityType => "SicknessCategory";
    Guid IAuditEvent.EntityId => CategoryId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Sickness category '{Name}' deactivated";
    object? IAuditEvent.Before => new { IsActive = true };
    object? IAuditEvent.After => new { IsActive = false };
    object? IAuditEvent.Metadata => null;
}
