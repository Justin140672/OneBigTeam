using HR.SharedKernel;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// Follow-up C: tracks the one-off internal-operations notification email for a single newly-opened
/// administrative alert (missing-file organisation data export failures). Exactly one row per alert
/// id — <see cref="AlertId"/> is the idempotency key (unique index) so re-enqueuing the send job for
/// the same alert (ordinary Hangfire retry, reconciliation, or a duplicate enqueue from concurrent
/// alert creation) can never send a second email. A recurrence of an already-open alert never
/// creates a row here (the writer only enqueues on first open), and an identical failure after
/// resolution opens a brand-new alert with a new id, so it gets its own delivery row and email.
///
/// <para>Follow-up E: delivery is made <b>exclusive and recoverable</b>. Before the Postmark call a
/// worker must <see cref="Claim"/> the row — this moves it to <see cref="EmailDeliveryStatus.Sending"/>,
/// increments <see cref="AttemptCount"/> and takes a bounded ownership lease
/// (<see cref="LeaseMinutes"/>). The claim is persisted under the <c>xmin</c> optimistic-concurrency
/// token, so of two jobs racing to send only one commits the claim; the other backs off as a no-op.
/// A second worker cannot claim a row whose lease is still live. If the owner crashes mid-send the
/// lease expires and <see cref="Claim"/> permits re-claiming (up to <see cref="MaxAttempts"/>). A
/// transient failure calls <see cref="ReleaseForRetry"/> (back to Pending) so a Hangfire retry or the
/// reconciliation sweep can pick it up; once attempts are exhausted the row is permanently
/// <see cref="EmailDeliveryStatus.Failed"/> and never retried again.</para>
///
/// <para>Exactly-once is <b>not</b> claimed: Postmark's send endpoint has no client-supplied
/// idempotency key, so a crash in the narrow window between "Postmark accepted" and "status
/// persisted as Sent" can yield a second send on recovery. This is at-least-once delivery to an
/// internal operations mailbox and is documented as an accepted limitation.</para>
/// </summary>
internal sealed class OperationalAlertEmailDelivery
{
    /// <summary>Follow-up E: initial send plus interruption-recovery retries before the row is failed permanently.</summary>
    public const int MaxAttempts = 4;

    /// <summary>
    /// Follow-up E: how long a worker's ownership lease on an in-flight send lasts before the
    /// reconciliation sweep (or a Hangfire retry) may re-claim it. Comfortably longer than a single
    /// Postmark call plus its retry budget, so a healthy send is never reclaimed under it.
    /// </summary>
    public const int LeaseMinutes = 10;

    private OperationalAlertEmailDelivery() { }

    public Guid Id { get; private set; }
    public Guid AlertId { get; private set; }
    public Guid CompanyId { get; private set; }
    public EmailDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Follow-up E: opaque token identifying the worker that currently owns an in-flight send.</summary>
    public Guid? LeaseOwnerToken { get; private set; }

    /// <summary>Follow-up E: when the current owner acquired its lease.</summary>
    public DateTimeOffset? LeaseAcquiredAt { get; private set; }

    /// <summary>Follow-up E: when the current owner's lease expires and the send may be re-claimed.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public static OperationalAlertEmailDelivery Create(Guid id, Guid alertId, Guid companyId, DateTimeOffset now) => new()
    {
        Id = id,
        AlertId = alertId,
        CompanyId = companyId,
        Status = EmailDeliveryStatus.Pending,
        AttemptCount = 0,
        CreatedAt = now,
    };

    /// <summary>Follow-up E: no worker holds a live ownership lease (never leased, or the lease has expired).</summary>
    public bool IsLeaseExpired(DateTimeOffset now) => LeaseExpiresAt is not { } expires || expires <= now;

    /// <summary>Follow-up E: a terminal state that must never be retried or re-claimed.</summary>
    public bool IsTerminal =>
        Status is EmailDeliveryStatus.Sent or EmailDeliveryStatus.Failed or EmailDeliveryStatus.Skipped;

    /// <summary>Follow-up E: at least one more send attempt is permitted before the row is failed permanently.</summary>
    public bool HasAttemptsRemaining => AttemptCount < MaxAttempts;

    /// <summary>
    /// Ticket 3L: a worker currently holds a <b>live</b> ownership lease on an in-flight send — the row
    /// is <see cref="EmailDeliveryStatus.Sending"/> and the lease has not expired. While this is true no
    /// other job (a duplicate send execution, the reconciliation sweep) may fail, re-claim or otherwise
    /// disturb the row, <i>regardless of the attempt budget</i>. This is the single rule both jobs use
    /// so a duplicate can never mark the row Failed while the real sender is mid-send on its final
    /// attempt (which would race <see cref="MarkSent"/> and silently drop a delivered email).
    /// </summary>
    public bool HasLiveOwner(DateTimeOffset now) =>
        Status == EmailDeliveryStatus.Sending && !IsLeaseExpired(now);

    /// <summary>
    /// Ticket 3L: permanently fail the delivery, but only when no worker still holds a live ownership
    /// lease (see <see cref="HasLiveOwner"/>) and the row is not already terminal. Returns a conflict
    /// <see cref="Result"/> otherwise so the caller leaves the row untouched. Shared by
    /// <see cref="Jobs.SendOperationalAlertEmailJob"/> and
    /// <see cref="Jobs.ReconcileStalledOperationalAlertEmailDeliveriesJob"/>.
    /// </summary>
    public Result MarkFailedIfNoLiveOwner(string reason, DateTimeOffset now)
    {
        if (HasLiveOwner(now))
            return Result.Failure(Error.Conflict(
                $"Delivery for alert '{AlertId}' is owned by another worker until {LeaseExpiresAt:o}."));

        if (IsTerminal)
            return Result.Failure(Error.Conflict(
                $"Delivery for alert '{AlertId}' is already in terminal state '{Status}'."));

        MarkFailed(reason);
        return Result.Success();
    }

    /// <summary>
    /// Follow-up E: atomically claim this delivery for a send attempt and take an ownership lease.
    /// Allowed from <see cref="EmailDeliveryStatus.Pending"/>, or from
    /// <see cref="EmailDeliveryStatus.Sending"/> only once the previous owner's lease has expired
    /// (a genuine interruption). Rejected while the row is terminal, while another worker still holds
    /// a live lease, or once <see cref="MaxAttempts"/> is reached. The caller must persist the change
    /// under the concurrency token and treat a <c>DbUpdateConcurrencyException</c> as "another worker
    /// won the claim — do nothing".
    /// </summary>
    public Result Claim(Guid ownerToken, DateTimeOffset now)
    {
        if (ownerToken == Guid.Empty)
            return Result.Failure(Error.Validation("A send attempt requires a non-empty owner token."));

        if (IsTerminal)
            return Result.Failure(Error.Conflict($"Delivery for alert '{AlertId}' is already in terminal state '{Status}'."));

        if (Status == EmailDeliveryStatus.Sending && !IsLeaseExpired(now))
            return Result.Failure(Error.Conflict(
                $"Delivery for alert '{AlertId}' is owned by another worker until {LeaseExpiresAt:o}."));

        if (!HasAttemptsRemaining)
            return Result.Failure(Error.Conflict(
                $"Delivery for alert '{AlertId}' has exhausted its {MaxAttempts} attempts."));

        Status = EmailDeliveryStatus.Sending;
        AttemptCount++;
        LastAttemptAt = now;
        LeaseOwnerToken = ownerToken;
        LeaseAcquiredAt = now;
        LeaseExpiresAt = now.AddMinutes(LeaseMinutes);
        return Result.Success();
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = EmailDeliveryStatus.Sent;
        SentAt = now;
        FailureReason = null;
        ClearLease();
    }

    /// <summary>
    /// Follow-up E: a transient send failure with attempts still remaining — return the row to
    /// <see cref="EmailDeliveryStatus.Pending"/> and drop the lease so a Hangfire retry or the
    /// reconciliation sweep can re-claim and retry it. No-op if the row is no longer Sending.
    /// </summary>
    public void ReleaseForRetry(DateTimeOffset now)
    {
        if (Status != EmailDeliveryStatus.Sending)
            return;

        Status = EmailDeliveryStatus.Pending;
        ClearLease();
    }

    /// <summary>Reason must already be a short sanitised category — never a raw exception message.</summary>
    public void MarkFailed(string reason)
    {
        Status = EmailDeliveryStatus.Failed;
        FailureReason = reason;
        ClearLease();
    }

    /// <summary>Expected non-delivery (no internal recipient configured) — final, never retried.</summary>
    public void MarkSkipped(string reason)
    {
        Status = EmailDeliveryStatus.Skipped;
        FailureReason = reason;
        ClearLease();
    }

    private void ClearLease()
    {
        LeaseOwnerToken = null;
        LeaseAcquiredAt = null;
        LeaseExpiresAt = null;
    }
}
