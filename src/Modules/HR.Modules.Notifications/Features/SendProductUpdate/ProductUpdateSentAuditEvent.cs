using HR.SharedKernel;

namespace HR.Modules.Notifications.Features.SendProductUpdate;

/// <summary>
/// Customer Release Notifications: audit trail for a manual platform-admin product/release
/// announcement send. Actor is the sending platform administrator. Spans every active customer
/// company, so — unlike most audit events — there is no single owning CompanyId; BatchId (shared as
/// SourceEntityId across every Notification row this send created) is the correlating identifier.
/// </summary>
internal sealed record ProductUpdateSentAuditEvent(
    Guid BatchId,
    Guid? ActorUserId,
    string Title,
    int RecipientCount,
    int CompanyCount,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "notifications.product_update_sent";
    string IAuditEvent.EntityType => "ProductUpdate";
    Guid IAuditEvent.EntityId => BatchId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => BatchId;
    string? IAuditEvent.Summary => $"Product update \"{Title}\" sent to {RecipientCount} Company Administrator(s) across {CompanyCount} compan{(CompanyCount == 1 ? "y" : "ies")}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Title, RecipientCount, CompanyCount };
    object? IAuditEvent.Metadata => null;
}
