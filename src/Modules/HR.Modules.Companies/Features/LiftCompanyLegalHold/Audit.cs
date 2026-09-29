using HR.SharedKernel;

namespace HR.Modules.Companies.Features.LiftCompanyLegalHold;

internal sealed record CompanyLegalHoldLiftedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.legal-hold-lifted";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Legal hold lifted. Retention deletion may resume for this company. Reason: {Reason}";
    object? IAuditEvent.Before => new { LegalHoldActive = true };
    object? IAuditEvent.After => new { LegalHoldActive = false };
    object? IAuditEvent.Metadata => new { Reason };
}
