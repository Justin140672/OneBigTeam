using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

/// <summary>
/// Records a platform-administrator scheduling a company for permanent deletion. Uses the same
/// cross-cutting IAuditEventPublisher as every other audited action in this module (see
/// ExtendCustomerTrial's Audit.cs remarks). Surfaced on the Platform Audit Log
/// (/audit-log) alongside CancelCustomerDeletion and ExecuteCustomerDeletion.
/// </summary>
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
