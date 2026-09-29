using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Companies.Domain;

internal sealed class CustomerSubscription
{
    private CustomerSubscription() { }

    public Guid CompanyId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset TrialStartedAt { get; private set; }
    public DateTimeOffset TrialExpiresAt { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public string? PriceId { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public string? LastAppliedStripeEventId { get; private set; }
    public DateTimeOffset? LastAppliedStripeEventCreatedAt { get; private set; }

    public bool AdminForcedReadOnly { get; private set; }

    /// <summary>
    /// Permanent Deletion Queue (Customer Lifecycle epic) — a company being scheduled for
    /// permanent deletion is a separate lifecycle overlay layered on top of the existing
    /// trial/subscription state machine, not a replacement for it (mirrors how AdminForcedReadOnly
    /// above is an independent overlay rather than a new SubscriptionStatus value). A non-null
    /// DeletionScheduledAt means a deletion countdown is (or was) active; DeletionCancelledAt and
    /// DeletionExecutedAt are mutually-exclusive terminal outcomes of that countdown, both kept
    /// (rather than clearing DeletionScheduledAt) so the queue can show history/cancellation status
    /// for a company even after the countdown ends. Deliberately persisted columns rather than a
    /// computed status, per project convention (see MarkExpiredIfNeeded remarks).
    /// </summary>
    public DateTimeOffset? DeletionScheduledAt { get; private set; }
    public Guid? DeletionScheduledBy { get; private set; }
    public DateTimeOffset? DeletionCancelledAt { get; private set; }
    public DateTimeOffset? DeletionExecutedAt { get; private set; }

    /// <summary>
    /// NFR-07 legal hold. A non-null <see cref="LegalHoldPlacedAt"/> means this company's data must
    /// be preserved (litigation hold, regulatory investigation, dispute) and every automated or
    /// operator-triggered retention deletion path must skip it — enforced by
    /// <see cref="ILegalHoldStatusReader"/> in the retention/purge handlers and jobs, and by
    /// <see cref="ExecuteDeletion"/> refusing to run while a hold is in place. A minimal flag on the
    /// platform-owned subscription record rather than a new table: a hold is company-wide and
    /// coarse-grained by design (per the NFR-07 "minimal legal_hold concept" scope). Deliberately
    /// persisted columns, matching the deletion-overlay convention above.
    /// </summary>
    public DateTimeOffset? LegalHoldPlacedAt { get; private set; }
    public Guid? LegalHoldPlacedBy { get; private set; }
    public string? LegalHoldReason { get; private set; }

    /// <summary>
    /// Ticket 2: indicates whether this customer is an original customer from before a specific
    /// product launch date. Used to filter communication for new-product updates — original
    /// customers may not have access to new products yet, so they should not receive product
    /// update communications intended for new signups or upsell audiences.
    /// </summary>
    public bool IsOriginalCustomer { get; private set; }

    public bool IsUnderLegalHold => LegalHoldPlacedAt is not null;

    public bool HasPendingDeletion =>
        DeletionScheduledAt is not null && DeletionCancelledAt is null && DeletionExecutedAt is null;

    public static CustomerSubscription StartTrial(Guid companyId, DateTimeOffset now, int trialLengthDays)
    {
        return new CustomerSubscription
        {
            CompanyId = companyId,
            Status = SubscriptionStatus.Trial,
            TrialStartedAt = now,
            TrialExpiresAt = now.AddDays(trialLengthDays),
            CancelAtPeriodEnd = false,
            IsOriginalCustomer = false,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// OBT-REM-09: true when a Stripe event with the given creation timestamp/id is older than (or a
    /// deterministic tie-break loser against) the last event actually applied to this subscription.
    /// A stale event must be recorded as processed but must NOT be projected.
    ///
    /// <para>
    /// Deterministic tie-break for two different events sharing the same
    /// <c>EventCreatedAt</c> (Stripe timestamps only have second resolution, so this is not
    /// theoretical): the event whose id sorts LATER using ordinal string comparison wins. This has
    /// no special meaning to Stripe, but it is cheap, requires no extra Stripe API call, and — most
    /// importantly — is deterministic and commutative, so replaying either event in either order
    /// always converges on the same winner.
    /// </para>
    /// </summary>
    public bool IsStaleStripeEvent(string? eventId, DateTimeOffset? eventCreatedAt)
    {
        if (eventCreatedAt is null)
            return false;

        if (LastAppliedStripeEventCreatedAt is null)
            return false;

        if (eventCreatedAt.Value > LastAppliedStripeEventCreatedAt.Value)
            return false;

        if (eventCreatedAt.Value < LastAppliedStripeEventCreatedAt.Value)
            return true;

        if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(LastAppliedStripeEventId))
            return false;

        return string.CompareOrdinal(eventId, LastAppliedStripeEventId) <= 0;
    }

    /// <summary>
    /// Ticket 9 (P2): true when <paramref name="eventId"/>/<paramref name="eventCreatedAt"/> is a
    /// genuinely different event from the last one applied, but shares the same creation timestamp
    /// (Stripe timestamps only have second resolution) — there is no reliable signal for which of
    /// the two actually happened "first" from the webhook payloads alone. IsStaleStripeEvent used to
    /// resolve this purely via <c>CompareOrdinal</c> on the event id, which produces a deterministic
    /// winner but has no relationship to which event Stripe considers authoritative — a
    /// less-complete "checkout.session.completed" delivery could win over a richer
    /// "customer.subscription.updated" delivery (or vice versa) purely by chance of id ordering.
    /// Callers should reconcile against the live Stripe subscription (see
    /// IStripeGateway.GetSubscriptionAsync) rather than trust either payload's fields directly when
    /// this returns true.
    /// </summary>
    public bool IsAmbiguousWithLastApplied(string? eventId, DateTimeOffset? eventCreatedAt)
    {
        if (eventCreatedAt is null || LastAppliedStripeEventCreatedAt is null)
            return false;

        if (eventCreatedAt.Value != LastAppliedStripeEventCreatedAt.Value)
            return false;

        return !string.Equals(eventId, LastAppliedStripeEventId, StringComparison.Ordinal);
    }

    private void RecordAppliedStripeEvent(string? eventId, DateTimeOffset? eventCreatedAt, DateTimeOffset now)
    {
        if (eventCreatedAt is not null)
        {
            LastAppliedStripeEventId = eventId;
            LastAppliedStripeEventCreatedAt = eventCreatedAt;
        }

        Version++;
        UpdatedAt = now;
    }

    /// <summary>
    /// Idempotently transitions Trial -> TrialExpired once <paramref name="now"/> has reached
    /// TrialExpiresAt. Returns true only when a transition actually happened, so callers know
    /// whether the change needs persisting (SaveChangesAsync). Deliberately a persisted, auditable
    /// transition rather than a purely computed property — matches project convention of avoiding
    /// clock-skew surprises from inline-only expiry checks.
    /// </summary>
    public bool MarkExpiredIfNeeded(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trial || now < TrialExpiresAt)
            return false;

        Status = SubscriptionStatus.TrialExpired;
        Version++;
        UpdatedAt = now;
        return true;
    }

    public void ActivateSubscription(
        string stripeCustomerId,
        string stripeSubscriptionId,
        string priceId,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset now,
        string? stripeEventId = null,
        DateTimeOffset? stripeEventCreatedAt = null)
    {
        Status = SubscriptionStatus.Active;
        StripeCustomerId = stripeCustomerId;
        StripeSubscriptionId = stripeSubscriptionId;
        PriceId = priceId;
        CurrentPeriodEnd = currentPeriodEnd;
        CancelAtPeriodEnd = false;
        RecordAppliedStripeEvent(stripeEventId, stripeEventCreatedAt, now);
    }

    public void UpdateFromStripe(
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEnd,
        bool cancelAtPeriodEnd,
        DateTimeOffset now,
        string? stripeEventId = null,
        DateTimeOffset? stripeEventCreatedAt = null)
    {
        Status = status;
        CurrentPeriodEnd = currentPeriodEnd;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        RecordAppliedStripeEvent(stripeEventId, stripeEventCreatedAt, now);
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        CancelAtPeriodEnd = true;
        Version++;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        CancelAtPeriodEnd = false;
        Version++;
        UpdatedAt = now;
    }

    public Result ExtendTrial(DateTimeOffset newTrialExpiresAt, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trial && Status != SubscriptionStatus.TrialExpired)
            return Result.Failure(Error.Validation("Only trial subscriptions can have their trial extended."));

        if (newTrialExpiresAt <= now)
            return Result.Failure(Error.Validation("The new trial expiry date must be in the future."));

        TrialExpiresAt = newTrialExpiresAt;
        Status = SubscriptionStatus.Trial;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result AdminCancelAtPeriodEnd(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active && Status != SubscriptionStatus.PastDue)
            return Result.Failure(Error.Validation("Only an active or past-due subscription can be cancelled."));

        CancelAtPeriodEnd = true;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ReinstateCancelledSubscription(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Canceled && !CancelAtPeriodEnd)
        {
            return Result.Failure(Error.Validation(
                "This subscription is not cancelled or scheduled to cancel."));
        }

        if (Status == SubscriptionStatus.Canceled)
        {
            Status = SubscriptionStatus.Active;
        }

        CancelAtPeriodEnd = false;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ForceReadOnly(DateTimeOffset now)
    {
        if (AdminForcedReadOnly)
            return Result.Failure(Error.Validation("This company is already in forced read-only mode."));

        AdminForcedReadOnly = true;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ResumeService(DateTimeOffset now)
    {
        if (!AdminForcedReadOnly)
            return Result.Failure(Error.Validation("This company is not currently in forced read-only mode."));

        AdminForcedReadOnly = false;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ScheduleDeletion(Guid? scheduledByUserId, DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        if (DeletionExecutedAt is not null)
            return Result.Failure(Error.Validation("This company's deletion has already been executed."));

        if (scheduledFor <= now)
            return Result.Failure(Error.Validation("The scheduled deletion date must be in the future."));

        DeletionScheduledAt = scheduledFor;
        DeletionScheduledBy = scheduledByUserId;
        DeletionCancelledAt = null;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result CancelScheduledDeletion(DateTimeOffset now)
    {
        if (!HasPendingDeletion)
            return Result.Failure(Error.Validation("This company does not have a pending deletion to cancel."));

        DeletionCancelledAt = now;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Platform-administrator action: executes a pending deletion now, ahead of (or at) the
    /// scheduled date. Scope note (see lead report): this is deliberately a SAFE, REVERSIBLE status
    /// transition only — it marks the company as deletion-executed and revokes access by forcing
    /// read-only mode (reusing ForceReadOnly's existing AdminForcedReadOnly overlay), the same way
    /// Login-As-Customer was scoped conservatively. It does NOT hard-delete the company's actual
    /// data rows across other modules (employees, documents, etc.) — that is a materially larger,
    /// irreversible, cross-module cascade that deserves its own dedicated story and review, not a
    /// side effect bolted onto this one.
    /// </summary>
    /// <summary>
    /// NFR-07: places a company-wide legal hold. Idempotent-ish — re-placing an existing hold
    /// refreshes the reason/actor/date rather than failing, so an operator can update the reason as
    /// a matter develops. While a hold is in place, retention deletion (automated jobs and the
    /// operator purge endpoints) skips this company and <see cref="ExecuteDeletion"/> is blocked.
    /// </summary>
    public Result PlaceLegalHold(Guid? placedByUserId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("A legal hold requires a reason."));

        LegalHoldPlacedAt = now;
        LegalHoldPlacedBy = placedByUserId;
        LegalHoldReason = reason.Trim();
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result LiftLegalHold(DateTimeOffset now)
    {
        if (!IsUnderLegalHold)
            return Result.Failure(Error.Validation("This company is not currently under a legal hold."));

        LegalHoldPlacedAt = null;
        LegalHoldPlacedBy = null;
        LegalHoldReason = null;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Ticket 2: updates the original customer classification status. Platform-administrator action
    /// to tag or untag a customer as an original customer (one who existed before a product launch).
    /// Used for filtering product update communications.
    /// </summary>
    public Result SetOriginalCustomerStatus(bool isOriginal, Guid? updatedByUserId, DateTimeOffset now)
    {
        if (updatedByUserId is null)
            return Result.Failure(Error.Validation("The user ID must be provided."));

        IsOriginalCustomer = isOriginal;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result ExecuteDeletion(DateTimeOffset now)
    {
        if (!HasPendingDeletion)
        {
            return Result.Failure(Error.Validation(
                "This company does not have a pending deletion to execute."));
        }

        if (IsUnderLegalHold)
        {
            return Result.Failure(Error.Conflict(
                "This company is under a legal hold. Lift the legal hold before executing deletion."));
        }

        DeletionExecutedAt = now;
        AdminForcedReadOnly = true;
        Version++;
        UpdatedAt = now;
        return Result.Success();
    }
}
