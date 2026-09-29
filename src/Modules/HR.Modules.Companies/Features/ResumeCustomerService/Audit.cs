using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ResumeCustomerService;

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
