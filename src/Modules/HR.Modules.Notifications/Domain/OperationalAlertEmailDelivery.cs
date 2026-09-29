using HR.SharedKernel;

namespace HR.Modules.Notifications.Domain;

internal sealed class OperationalAlertEmailDelivery
{
    public const int MaxAttempts = 4;

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

    public Guid? LeaseOwnerToken { get; private set; }

    public DateTimeOffset? LeaseAcquiredAt { get; private set; }

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

    public bool IsLeaseExpired(DateTimeOffset now) => LeaseExpiresAt is not { } expires || expires <= now;

    public bool IsTerminal =>
        Status is EmailDeliveryStatus.Sent or EmailDeliveryStatus.Failed or EmailDeliveryStatus.Skipped;

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

    public void ReleaseForRetry(DateTimeOffset now)
    {
        if (Status != EmailDeliveryStatus.Sending)
            return;

        Status = EmailDeliveryStatus.Pending;
        ClearLease();
    }

    public void MarkFailed(string reason)
    {
        Status = EmailDeliveryStatus.Failed;
        FailureReason = reason;
        ClearLease();
    }

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
