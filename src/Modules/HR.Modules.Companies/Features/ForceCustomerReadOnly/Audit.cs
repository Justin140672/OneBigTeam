using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

internal sealed record ReadOnlyModeForcedByAdminAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.admin-forced-read-only";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Read-only mode forced by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => new { AdminForcedReadOnly = false };
    object? IAuditEvent.After => new { AdminForcedReadOnly = true };
    object? IAuditEvent.Metadata => new { Reason };
}
