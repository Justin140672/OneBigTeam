using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

/// <summary>
/// Records a support session being redeemed (Support epic). Plugs into the existing
/// cross-cutting IAuditEventPublisher/AuditDbContext infrastructure, same as
/// HR.Modules.Companies.Features.ExtendCustomerTrial.Audit. This endpoint is anonymous
/// (token-gated, single-use, 256-bit entropy credential), so the audit trail is the primary
/// record of who accessed the customer's support context and when.
/// </summary>
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
