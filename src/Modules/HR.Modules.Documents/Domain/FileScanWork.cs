using HR.SharedKernel;

namespace HR.Modules.Documents.Domain;

internal enum FileScanWorkState
{
    Pending = 0,
    Scanning = 1,
    Completed = 2,
    Exhausted = 3,
}

/// <summary>
/// Durable scan work item (transactional outbox). Staged in the same transaction as the upload that
/// needs scanning, so a crash between commit and Hangfire enqueue never loses the scan. The job claims
/// it with a time-boxed lease token; completion is only honoured for the current lease holder, so
/// duplicate or stale jobs can never overwrite a later result.
/// </summary>
internal sealed class FileScanWork : IVersionedAggregate
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    private FileScanWork() { }

    public Guid Id { get; private set; }
    public FileScanTargetType TargetType { get; private set; }
    public Guid EntityId { get; private set; }
    public Guid CompanyId { get; private set; }
    public FileScanWorkState State { get; private set; }
    public int AttemptCount { get; private set; }
    public int DispatchCount { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastDispatchedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public static FileScanWork Create(FileScanTargetType targetType, Guid entityId, Guid companyId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TargetType = targetType,
        EntityId = entityId,
        CompanyId = companyId,
        State = FileScanWorkState.Pending,
        NextAttemptAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Restage(DateTimeOffset now)
    {
        State = FileScanWorkState.Pending;
        AttemptCount = 0;
        LeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = now;
        LastError = null;
        CompletedAt = null;
        UpdatedAt = now;
    }

    public bool IsClaimable(DateTimeOffset now) =>
        State == FileScanWorkState.Pending
        || (State == FileScanWorkState.Scanning && (LeaseExpiresAt is null || LeaseExpiresAt <= now));

    public bool HoldsLease(Guid token) => State == FileScanWorkState.Scanning && LeaseToken == token;

    public bool HasExhaustedAttempts => AttemptCount >= MaxAttempts;

    public Guid Claim(DateTimeOffset now)
    {
        var token = Guid.NewGuid();
        State = FileScanWorkState.Scanning;
        LeaseToken = token;
        LeaseExpiresAt = now + LeaseDuration;
        AttemptCount++;
        UpdatedAt = now;
        return token;
    }

    public void Release(string safeError, DateTimeOffset now)
    {
        State = FileScanWorkState.Pending;
        LeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = now + RetryDelay;
        LastError = safeError;
        UpdatedAt = now;
    }

    public void ReleaseForImmediateRetry(string reason, DateTimeOffset now)
    {
        State = FileScanWorkState.Pending;
        LeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = now;
        LastError = reason;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        State = FileScanWorkState.Completed;
        LeaseToken = null;
        LeaseExpiresAt = null;
        LastError = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Exhaust(string safeError, DateTimeOffset now)
    {
        State = FileScanWorkState.Exhausted;
        LeaseToken = null;
        LeaseExpiresAt = null;
        LastError = safeError;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void RecordDispatch(DateTimeOffset now, TimeSpan redispatchDelay)
    {
        DispatchCount++;
        LastDispatchedAt = now;
        NextAttemptAt = now + redispatchDelay;
        UpdatedAt = now;
    }
}
