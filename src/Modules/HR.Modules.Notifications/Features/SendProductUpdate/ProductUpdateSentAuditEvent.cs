using HR.SharedKernel;

namespace HR.Modules.Notifications.Features.SendProductUpdate;

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
