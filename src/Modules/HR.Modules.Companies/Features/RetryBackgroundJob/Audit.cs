using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RetryBackgroundJob;

internal sealed record BackgroundJobRetriedByAdminAuditEvent(
    string JobId,
    string JobName,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason,
    bool Success,
    string? Error) : IAuditEvent
{
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
