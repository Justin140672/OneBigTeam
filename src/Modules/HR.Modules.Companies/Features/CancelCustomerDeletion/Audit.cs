using HR.SharedKernel;

namespace HR.Modules.Companies.Features.CancelCustomerDeletion;

internal sealed record CustomerDeletionCancelledAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.deletion-cancelled";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Scheduled permanent deletion cancelled. Reason: {Reason}";
    object? IAuditEvent.Before => new { DeletionCancelledAt = (DateTimeOffset?)null };
    object? IAuditEvent.After => new { DeletionCancelledAt = OccurredAt };
    object? IAuditEvent.Metadata => new { Reason };
}
