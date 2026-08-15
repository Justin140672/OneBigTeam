using HR.SharedKernel;

namespace HR.Modules.Companies.Features.AdminCancelSubscription;

internal sealed record AdminCancelSubscriptionAuditSnapshot(string Status, bool CancelAtPeriodEnd);

/// <summary>
/// Records a platform-administrator-initiated cancellation (support intervention — e.g. handling
/// a customer complaint directly). Uses the same cross-cutting IAuditEventPublisher as every other
/// audited action in this module (see ExtendCustomerTrial's Audit.cs remarks).
/// </summary>
internal sealed record SubscriptionCancelledByAdminAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason,
    AdminCancelSubscriptionAuditSnapshot PreviousState,
    AdminCancelSubscriptionAuditSnapshot CurrentState) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.admin-cancelled";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Subscription cancelled (at period end) by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => new { Reason };
}
