using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed record SupportSessionRedeemedAuditEvent(
    Guid CompanyId,
    Guid SupportSessionId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "support.session-redeemed";
    string IAuditEvent.EntityType => "SupportSession";
    Guid IAuditEvent.EntityId => SupportSessionId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Support session redeemed.";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
