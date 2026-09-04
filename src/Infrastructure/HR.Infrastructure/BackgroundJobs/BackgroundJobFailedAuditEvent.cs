using System.Security.Cryptography;
using System.Text;
using HR.SharedKernel;

namespace HR.Infrastructure.BackgroundJobs;

/// <summary>
/// Fixed, non-PII-carrying failure categories for background jobs. Deliberately coarse — this
/// exists purely to say "what kind of thing went wrong" for triage without ever risking a name,
/// email, file path, medical detail or other free-text fragment ending up in the audit trail.
/// </summary>
internal enum BackgroundJobFailureCategory
{
    Unknown,
    Timeout,
    ExternalServiceError,
    DatabaseError,
    ValidationOrDataIntegrityError,
    NotFound,
    Cancelled,
}

/// <summary>
/// Audit record for a background job that failed after exhausting (or without) retries.
///
/// <para>
/// OBT-REM-11: <see cref="Exception.Message"/> is never persisted here — not even after regex
/// scrubbing (<c>SensitiveDataScrubber.ScrubText</c> only strips a fixed set of known-shaped
/// values such as NI numbers/IBANs/tokens; it does not reliably remove names, emails, file paths
/// or free-text medical detail that can appear in an arbitrary exception message). Only safe,
/// structured, closed-set fields are stored: Hangfire job id, job type, method name, exception
/// <b>type</b> name, and a fixed <see cref="BackgroundJobFailureCategory"/> derived from the
/// exception's .NET type. Full exception detail (message + stack trace) remains only in the
/// operational logs (see <c>BackgroundJobLoggingFilter</c>), which have appropriate access
/// restrictions — never in the audit trail.
/// </para>
///
/// <para>
/// Scoping: a tenant-specific job passes its own <c>companyId</c> as an explicit Hangfire job
/// argument (validated against the loaded entity inside the job itself — see e.g.
/// <c>EmailDeliveryJob.SendAsync</c>) so this event can reliably scope the audit row to that
/// tenant; a job with no <c>companyId</c> argument is a deliberately system-wide job (a
/// cross-company recurring sweep) and produces a row with <see cref="Guid.Empty"/> and
/// <c>Scope = "system-wide"</c>. The <see cref="IAuditEvent.EventId"/> is derived deterministically
/// from the Hangfire job id, so a job that fails and is retried (including via a duplicate
/// "OnPerformed" callback for the same attempt) yields exactly one failure audit row, not one per
/// attempt/callback.
/// </para>
/// </summary>
internal sealed class BackgroundJobFailedAuditEvent : IAuditEvent
{
    private readonly Guid _eventId;

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
        FailureCategory = Classify(exception);
        OccurredAt = occurredAt;
        CompanyId = companyId ?? Guid.Empty;
        IsTenantScoped = companyId is not null && companyId.Value != Guid.Empty;
        _eventId = DeterministicGuid($"BackgroundJob.Failed|{jobId}");
    }

    private string JobId { get; }
    private string JobType { get; }
    private string MethodName { get; }
    private string ExceptionType { get; }
    private BackgroundJobFailureCategory FailureCategory { get; }
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
        $"Background job '{JobType}.{MethodName}' (Hangfire ID: {JobId}) failed: {FailureCategory} ({ExceptionType}).";

    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;

    object? IAuditEvent.Metadata => new
    {
        HangfireJobId = JobId,
        JobType,
        MethodName,
        ExceptionType,
        FailureCategory = FailureCategory.ToString(),
        Scope = IsTenantScoped ? "tenant" : "system-wide",
    };

    /// <summary>
    /// Maps an exception to a fixed, closed-set category using only its .NET type — never its
    /// message — so the result can never carry free-text PII regardless of what a given failure
    /// happened to say.
    /// </summary>
    private static BackgroundJobFailureCategory Classify(Exception exception) => exception switch
    {
        OperationCanceledException => BackgroundJobFailureCategory.Cancelled,
        TimeoutException => BackgroundJobFailureCategory.Timeout,
        System.Net.Http.HttpRequestException => BackgroundJobFailureCategory.ExternalServiceError,
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => BackgroundJobFailureCategory.DatabaseError,
        Microsoft.EntityFrameworkCore.DbUpdateException => BackgroundJobFailureCategory.DatabaseError,
        KeyNotFoundException => BackgroundJobFailureCategory.NotFound,
        ArgumentException => BackgroundJobFailureCategory.ValidationOrDataIntegrityError,
        InvalidOperationException => BackgroundJobFailureCategory.ValidationOrDataIntegrityError,
        _ => BackgroundJobFailureCategory.Unknown,
    };

    private static Guid DeterministicGuid(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }
}
