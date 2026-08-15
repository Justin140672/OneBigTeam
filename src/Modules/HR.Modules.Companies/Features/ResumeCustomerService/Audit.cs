using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ResumeCustomerService;

/// <summary>
/// Records a platform-administrator resuming service after a forced read-only period (reverses
/// ForceCustomerReadOnly). Uses the same cross-cutting IAuditEventPublisher as every other audited
/// action in this module (see ExtendCustomerTrial's Audit.cs remarks).
/// </summary>
internal sealed record ServiceResumedByAdminAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.admin-resumed-service";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Service resumed (forced read-only lifted) by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => new { AdminForcedReadOnly = true };
    object? IAuditEvent.After => new { AdminForcedReadOnly = false };
    object? IAuditEvent.Metadata => new { Reason };
}
