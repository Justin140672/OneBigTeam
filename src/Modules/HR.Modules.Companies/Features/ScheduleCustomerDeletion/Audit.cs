using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

internal sealed record CustomerDeletionScheduledAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset DeletionScheduledAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.deletion-scheduled";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary =>
        $"Permanent deletion scheduled for {DeletionScheduledAt:dd MMM yyyy}. Reason: {Reason}";
    object? IAuditEvent.Before => new { DeletionScheduledAt = (DateTimeOffset?)null };
    object? IAuditEvent.After => new { DeletionScheduledAt };
    object? IAuditEvent.Metadata => new { Reason };
}
