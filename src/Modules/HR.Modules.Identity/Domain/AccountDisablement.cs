using HR.SharedKernel;

namespace HR.Modules.Identity.Domain;

/// <summary>
/// A durable, module-scoped record of a required <see cref="ApplicationUser"/> disablement
/// triggered by an employee's departure being finalised (see
/// Features/OnEmployeeDepartureFinalised/Handler.cs and Jobs/AccountDisablementJob.cs).
///
/// P1 fix: departure finalisation previously only flipped Employees.Employee.HasSystemAccess,
/// while authentication actually enforces ApplicationUser.IsActive (see
/// DisabledAccountMiddleware) — the two could disagree indefinitely. This record makes the
/// resulting ApplicationUser update durable and retryable (mirrors
/// HR.Modules.Companies.Domain.OutboxMessage's Pending -&gt; Processing -&gt; Processed|Failed shape),
/// so a transient failure never silently leaves a departed employee's account active, and success
/// is only ever recorded once the account used by authentication has actually been disabled.
///
/// Ticket 19 (P2): implements <see cref="IVersionedAggregate"/> so a claim (recorded via
/// <see cref="Claim"/>) is guarded by the SAME optimistic-concurrency primitive
/// (<see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/>) already used
/// platform-wide — previously this table had no concurrency token at all, so two reconciler
/// replicas (or a live dispatch racing a reconciliation sweep) could both read the same Pending row
/// and both enqueue/process it. <see cref="ClaimedBy"/> + <see cref="LeaseExpiresAt"/> record WHO
/// currently owns the row and for how long, so the worker itself can verify it still holds the
/// claim before performing the actual disablement, and an interrupted claim (holder crashed) is
/// recoverable once its lease expires — never stuck forever.
/// </summary>
internal sealed class AccountDisablement : IVersionedAggregate
{
    private AccountDisablement() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusProcessed  = "processed";
    public const string StatusFailed     = "failed";

    /// <summary>
    /// Ticket 19 (P2): default lease duration a claim is held for. Generous relative to how long
    /// AccountDisablementJob normally takes (milliseconds) so a genuinely still-running attempt is
    /// never preempted, while still bounding how long a crashed holder's claim blocks reconciliation.
    /// </summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationUserId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    // Explicit, persisted optimistic-concurrency token (ticket 19). See AssetCategory.Version for
    // the same established idiom.
    public int Version { get; private set; } = 1;

    /// <summary>Ticket 19 (P2): opaque identifier of whichever worker/reconciler instance currently
    /// owns this row (a live dispatch, this app instance's reconciliation sweep, etc.) — null when
    /// unclaimed.</summary>
    public Guid? ClaimedBy { get; private set; }

    /// <summary>Ticket 19 (P2): the claim above is only valid until this instant — after it elapses
    /// the row is claimable again regardless of <see cref="ClaimedBy"/>, recovering from a holder
    /// that crashed mid-attempt without needing a separate "stale Processing" heuristic.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>Ticket 19 (P2): set once <see cref="AttemptCount"/> exceeds the automatic retry
    /// ceiling — distinguishes "still eligible for another automatic sweep" from "requires an
    /// explicit administrative retry" (see <see cref="RecordManualRetry"/>). Account disablement is
    /// security-critical, so reaching this state must stay visible rather than being silently
    /// retried forever or silently dropped.</summary>
    public bool IsTerminallyFailed { get; private set; }

    public Guid? LastRetriedByActorId { get; private set; }
    public string? LastRetryReason { get; private set; }
    public DateTimeOffset? LastRetriedAt { get; private set; }

    public void IncrementVersion() => Version++;

    public static AccountDisablement CreatePending(
        Guid id,
        Guid companyId,
        Guid applicationUserId,
        Guid employeeId,
        DateTimeOffset requestedAt)
    {
        return new AccountDisablement
        {
            Id = id,
            CompanyId = companyId,
            ApplicationUserId = applicationUserId,
            EmployeeId = employeeId,
            Status = StatusPending,
            AttemptCount = 0,
            RequestedAt = requestedAt,
            Version = 1,
        };
    }

    /// <summary>
    /// Ticket 19 (P2): claims this row for <paramref name="workerId"/> for <see cref="LeaseDuration"/>
    /// and transitions it to Processing in the same call — callers must persist this via
    /// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/> with the
    /// version this instance was loaded at, so a concurrent claimant's save wins the race and this
    /// one observes <c>DbUpdateConcurrencyException</c> instead of silently double-processing.
    /// </summary>
    public void Claim(Guid workerId, DateTimeOffset now)
    {
        ClaimedBy = workerId;
        LeaseExpiresAt = now + LeaseDuration;
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = null;
    }

    /// <summary>Ticket 19 (P2): true when <paramref name="workerId"/> is the current, non-expired
    /// claim holder — the worker must verify this immediately before performing the actual
    /// disablement, so a lease that expired (and was reclaimed by someone else) can never result in
    /// two workers both believing they own the row.</summary>
    public bool IsClaimedBy(Guid workerId, DateTimeOffset now) =>
        ClaimedBy == workerId && LeaseExpiresAt is { } expiresAt && expiresAt > now;

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = null;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = StatusProcessed;
        ProcessedAt = now;
        FailureReason = null;
        ClaimedBy = null;
        LeaseExpiresAt = null;
    }

    /// <summary>Bounded automatic retry: reaching <paramref name="maxAutomaticAttempts"/> marks this
    /// terminally failed (ticket 19) — never reset by the reconciliation sweep again, requiring an
    /// explicit <see cref="RecordManualRetry"/> instead. Below that ceiling it remains a normal,
    /// automatically-retryable Failed row exactly as before.</summary>
    public void MarkFailed(string reason, DateTimeOffset now, int maxAutomaticAttempts)
    {
        Status = StatusFailed;
        LastAttemptAt = now;
        FailureReason = reason;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        if (AttemptCount >= maxAutomaticAttempts)
            IsTerminallyFailed = true;
    }

    /// <summary>Resets a Failed record back to Pending so a retry sweep can re-enqueue it. Refuses a
    /// terminally-failed record (ticket 19) — that state requires <see cref="RecordManualRetry"/>
    /// instead, so a permanently-stuck security-critical disablement stays visible for an operator
    /// rather than being silently retried forever.</summary>
    public void ResetForRetry()
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry an account disablement with status '{Status}'.");

        if (IsTerminallyFailed)
            throw new InvalidOperationException(
                "This account disablement has exhausted its automatic retry budget and requires an explicit manual retry.");

        Status = StatusPending;
        FailureReason = null;
    }

    /// <summary>
    /// Ticket 19 (P2): explicit administrative override for a terminally-failed record — the only
    /// path back to Pending once <see cref="IsTerminallyFailed"/> is set. Always records who
    /// authorised it and why, so a security-critical disablement's retry history stays auditable
    /// even though this domain method alone doesn't publish an audit event (the calling
    /// handler/endpoint is expected to do so, same as every other actor-attributed mutation).
    /// </summary>
    public void RecordManualRetry(Guid actorId, string reason, DateTimeOffset now)
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot manually retry an account disablement with status '{Status}'.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a manual retry.", nameof(reason));

        Status = StatusPending;
        FailureReason = null;
        IsTerminallyFailed = false;
        LastRetriedByActorId = actorId;
        LastRetryReason = reason;
        LastRetriedAt = now;
    }

    /// <summary>
    /// Ticket 10 (P1) / Ticket 19 (P2): resets a Processing record back to Pending after it has been
    /// detected as stuck — either its lease has expired (ticket 19: the authoritative signal now)
    /// or, defensively, it has no lease at all (a row written before this migration). Recovers from
    /// a crash that occurred mid-attempt, between <see cref="Claim"/> and the job's own
    /// MarkProcessed/MarkFailed call.
    /// </summary>
    public void ResetToPendingAfterInterruption()
    {
        if (Status != StatusProcessing)
            throw new InvalidOperationException(
                $"Cannot reset an account disablement with status '{Status}' as interrupted.");

        Status = StatusPending;
        ClaimedBy = null;
        LeaseExpiresAt = null;
    }
}
