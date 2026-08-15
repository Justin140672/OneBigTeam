using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ReinstateCustomerSubscription;

internal sealed record ReinstateSubscriptionAuditSnapshot(string Status, bool CancelAtPeriodEnd);

/// <summary>
/// Records a platform-administrator reinstatement of a cancelled/cancelling subscription. Uses the
/// same cross-cutting IAuditEventPublisher as every other audited action in this module (see
/// ExtendCustomerTrial's Audit.cs remarks).
/// </summary>
internal sealed record SubscriptionReinstatedByAdminAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason,
    ReinstateSubscriptionAuditSnapshot PreviousState,
    ReinstateSubscriptionAuditSnapshot CurrentState) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.admin-reinstated";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Subscription reinstated by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => new { Reason };
}
