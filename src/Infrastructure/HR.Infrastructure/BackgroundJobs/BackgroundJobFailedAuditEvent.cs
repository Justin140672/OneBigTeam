using HR.SharedKernel;

namespace HR.Infrastructure.BackgroundJobs;

internal sealed class BackgroundJobFailedAuditEvent : IAuditEvent
{
    private readonly Guid _entityId = Guid.NewGuid();

    public BackgroundJobFailedAuditEvent(
        string jobId,
        string jobType,
        string methodName,
        Exception exception,
        DateTimeOffset occurredAt)
    {
        JobId = jobId;
        JobType = jobType;
        MethodName = methodName;
        Exception = exception;
        OccurredAt = occurredAt;
    }

    private string JobId { get; }
    private string JobType { get; }
    private string MethodName { get; }
    private Exception Exception { get; }

    // System-level — background jobs run across all tenants; no single company context.
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "BackgroundJob.Failed";
    string IAuditEvent.EntityType => "BackgroundJob";
    Guid IAuditEvent.EntityId => _entityId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    public DateTimeOffset OccurredAt { get; }
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary =>
        $"Background job '{JobType}.{MethodName}' (Hangfire ID: {JobId}) failed: {Exception.Message}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new
    {
        HangfireJobId = JobId,
        JobType,
        MethodName,
        ExceptionType = Exception.GetType().Name,
        ExceptionMessage = Exception.Message,
    };
}
