using System.Security.Cryptography;
using System.Text;
using HR.SharedKernel;

namespace HR.Infrastructure.BackgroundJobs;

/// <summary>
/// Audit record for a background job that failed after exhausting (or without) retries.
///
/// <para>
/// OBT-REM-02: the raw <see cref="Exception.Message"/> is never persisted. Only a sanitised summary
/// (run through <see cref="SensitiveDataScrubber.ScrubText"/>) plus safe structured fields — Hangfire
/// job id, job type, method name and exception type — are stored. Detailed exception diagnostics
/// remain in the operational logs (see <c>BackgroundJobLoggingFilter</c>), not the audit trail.
/// </para>
///
/// <para>
/// Scoping: a tenant-aware job that carries a <c>companyId</c> argument produces a tenant-scoped
/// audit row; a system-wide job produces a row with <see cref="Guid.Empty"/> and
/// <c>Scope = "system-wide"</c> so the two are distinguishable. The <see cref="IAuditEvent.EventId"/>
/// is derived deterministically from the Hangfire job id, so a job that fails and is retried yields
/// exactly one failure audit row, not one per attempt.
/// </para>
/// </summary>
internal sealed class BackgroundJobFailedAuditEvent : IAuditEvent
{
    private readonly Guid _eventId;
    private readonly string _sanitisedExceptionMessage;

    public BackgroundJobFailedAuditEvent(
        string jobId,
        string jobType,
        string methodName,
        Exception exception,
        DateTimeOffset occurredAt,
        Guid? companyId)
    {
        JobId = jobId;
        JobType = jobType;
        MethodName = methodName;
        ExceptionType = exception.GetType().Name;
        _sanitisedExceptionMessage = SensitiveDataScrubber.ScrubText(exception.Message);
        OccurredAt = occurredAt;
        CompanyId = companyId ?? Guid.Empty;
        IsTenantScoped = companyId is not null && companyId.Value != Guid.Empty;
        _eventId = DeterministicGuid($"BackgroundJob.Failed|{jobId}");
    }

    private string JobId { get; }
    private string JobType { get; }
    private string MethodName { get; }
    private string ExceptionType { get; }
    private bool IsTenantScoped { get; }

    public Guid CompanyId { get; }
    public DateTimeOffset OccurredAt { get; }

    Guid IAuditEvent.EventId => _eventId;
    AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
    string IAuditEvent.EventType => "BackgroundJob.Failed";
    string IAuditEvent.EntityType => "BackgroundJob";
    Guid IAuditEvent.EntityId => _eventId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;

    string? IAuditEvent.Summary =>
        $"Background job '{JobType}.{MethodName}' (Hangfire ID: {JobId}) failed with {ExceptionType}: {_sanitisedExceptionMessage}";

    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;

    object? IAuditEvent.Metadata => new
    {
        HangfireJobId = JobId,
        JobType,
        MethodName,
        ExceptionType,
        ExceptionMessage = _sanitisedExceptionMessage,
        Scope = IsTenantScoped ? "tenant" : "system-wide",
    };

    private static Guid DeterministicGuid(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }
}
