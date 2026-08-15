using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RetryBackgroundJob;

/// <summary>
/// Records a platform-administrator manually retrying a failed background job — an administrative
/// intervention, so it is audited via the same cross-cutting IAuditEventPublisher as every other
/// audited admin action in this module (see ForceCustomerReadOnly's Audit.cs remarks). There is no
/// company/tenant associated with a platform-wide job, so EntityId is the Hangfire job id itself.
/// </summary>
internal sealed record BackgroundJobRetriedByAdminAuditEvent(
    string JobId,
    string JobName,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason,
    bool Success,
    string? Error) : IAuditEvent
{
    // Platform-wide action, not tied to any single tenant — same "no company" convention as other
    // cross-tenant admin actions would need; there is no CustomerSubscription/Company row involved.
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "background-job.admin-retried";
    string IAuditEvent.EntityType => "BackgroundJob";
    Guid IAuditEvent.EntityId => Guid.Empty;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary =>
        $"Background job '{JobName}' ({JobId}) retried by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => new { JobId, JobName, State = "Failed" };
    object? IAuditEvent.After => new { JobId, JobName, State = Success ? "Enqueued" : "Failed", Error };
    object? IAuditEvent.Metadata => new { Reason };
}
