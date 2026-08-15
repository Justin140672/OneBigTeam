using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RevokeSupportSession;

/// <summary>
/// Records a platform-administrator revoking an outstanding support session (Support epic).
/// Plugs into the existing cross-cutting IAuditEventPublisher/AuditDbContext infrastructure, same
/// as HR.Modules.Companies.Features.ExtendCustomerTrial.Audit.
/// </summary>
internal sealed record SupportSessionRevokedAuditEvent(
    Guid CompanyId,
    Guid SupportSessionId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "support.session-revoked";
    string IAuditEvent.EntityType => "SupportSession";
    Guid IAuditEvent.EntityId => SupportSessionId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Support session revoked by platform administrator.";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
