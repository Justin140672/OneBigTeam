using HR.SharedKernel;

namespace HR.Modules.Companies.Features.PlaceCompanyLegalHold;

internal sealed record CompanyLegalHoldPlacedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.legal-hold-placed";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Legal hold placed. Retention deletion is now suspended for this company. Reason: {Reason}";
    object? IAuditEvent.Before => new { LegalHoldPlacedAt = (DateTimeOffset?)null };
    object? IAuditEvent.After => new { LegalHoldPlacedAt = OccurredAt };
    object? IAuditEvent.Metadata => new { Reason };
}
