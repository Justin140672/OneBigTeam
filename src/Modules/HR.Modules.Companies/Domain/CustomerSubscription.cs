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

    /// <summary>
    /// Support/admin override that forces read-only mode regardless of trial/subscription status.
    /// Distinct from the automatic TrialExpired-driven read-only gate (see
    /// SubscriptionStatusReader.GetStatusAsync) — this is an explicit platform-administrator
    /// intervention (Subscription Management epic) for cases such as suspected abuse or a billing
    /// dispute, independent of where the customer sits in the trial/paid lifecycle.
    /// </summary>
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
    /// True while a deletion countdown is active and not yet cancelled or executed — the set of
    /// companies the /deletion-queue page and the dashboard's "pending permanent deletions" stat
    /// both count as pending.
    /// </summary>
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
            CreatedAt = now,
            UpdatedAt = now,
        };
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
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Activates a paid subscription following a successful Stripe checkout
    /// (checkout.session.completed webhook).
    /// </summary>
    public void ActivateSubscription(
        string stripeCustomerId,
        string stripeSubscriptionId,
        string priceId,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset now)
    {
        Status = SubscriptionStatus.Active;
        StripeCustomerId = stripeCustomerId;
        StripeSubscriptionId = stripeSubscriptionId;
        PriceId = priceId;
        CurrentPeriodEnd = currentPeriodEnd;
        CancelAtPeriodEnd = false;
        UpdatedAt = now;
    }

    /// <summary>
    /// Reconciles local state from a Stripe webhook payload (customer.subscription.updated/deleted).
    /// Idempotent — safe to call repeatedly with the same payload.
    /// </summary>
    public void UpdateFromStripe(
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEnd,
        bool cancelAtPeriodEnd,
        DateTimeOffset now)
    {
        Status = status;
        CurrentPeriodEnd = currentPeriodEnd;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        UpdatedAt = now;
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        CancelAtPeriodEnd = true;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        CancelAtPeriodEnd = false;
        UpdatedAt = now;
    }

    /// <summary>
    /// Platform-administrator support action: pushes TrialExpiresAt out to a new date. Valid while
    /// the subscription is still on a trial (Trial or TrialExpired) — a company already on a paid
    /// subscription has no trial to extend. Reactivates an expired trial (TrialExpired -> Trial) so
    /// the customer regains write access immediately.
    /// </summary>
    public Result ExtendTrial(DateTimeOffset newTrialExpiresAt, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trial && Status != SubscriptionStatus.TrialExpired)
            return Result.Failure(Error.Validation("Only trial subscriptions can have their trial extended."));

        if (newTrialExpiresAt <= now)
            return Result.Failure(Error.Validation("The new trial expiry date must be in the future."));

        TrialExpiresAt = newTrialExpiresAt;
        Status = SubscriptionStatus.Trial;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Platform-administrator support action mirroring the customer-initiated
    /// RequestCancellation above, for support-driven cancellations (e.g. handling a customer
    /// complaint over the phone).
    /// </summary>
    public Result AdminCancelAtPeriodEnd(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active && Status != SubscriptionStatus.PastDue)
            return Result.Failure(Error.Validation("Only an active or past-due subscription can be cancelled."));

        CancelAtPeriodEnd = true;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Platform-administrator support action to reinstate a subscription that is scheduled to
    /// cancel (CancelAtPeriodEnd) or has already fully cancelled. When the underlying Stripe
    /// subscription is still live (CancelAtPeriodEnd was pending), this simply reverses that flag —
    /// same effect as Resume above. When the subscription has already reached the terminal
    /// Canceled status (the Stripe subscription itself no longer exists), there is no live Stripe
    /// subscription left to resume; this reactivates the local record to Active as a support
    /// override so the customer regains access immediately while billing is reconciled manually
    /// (see CompaniesModule remarks / lead report for this documented assumption).
    /// </summary>
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
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Platform-administrator support action: forces read-only mode independent of trial/billing
    /// status (see AdminForcedReadOnly remarks).
    /// </summary>
    public Result ForceReadOnly(DateTimeOffset now)
    {
        if (AdminForcedReadOnly)
            return Result.Failure(Error.Validation("This company is already in forced read-only mode."));

        AdminForcedReadOnly = true;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Reverses ForceReadOnly above, restoring normal trial/billing-status-driven access.
    /// </summary>
    public Result ResumeService(DateTimeOffset now)
    {
        if (!AdminForcedReadOnly)
            return Result.Failure(Error.Validation("This company is not currently in forced read-only mode."));

        AdminForcedReadOnly = false;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Platform-administrator action: schedules this company for permanent deletion after a
    /// countdown period. Re-schedulable — calling this again while a countdown is already pending
    /// simply resets the target date (e.g. an admin extending or shortening the countdown), which
    /// is why there's no "already scheduled" failure case. Not valid once deletion has already been
    /// executed, since execution is treated as terminal (see ExecuteDeletion).
    /// </summary>
    public Result ScheduleDeletion(Guid? scheduledByUserId, DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        if (DeletionExecutedAt is not null)
            return Result.Failure(Error.Validation("This company's deletion has already been executed."));

        if (scheduledFor <= now)
            return Result.Failure(Error.Validation("The scheduled deletion date must be in the future."));

        DeletionScheduledAt = scheduledFor;
        DeletionScheduledBy = scheduledByUserId;
        DeletionCancelledAt = null;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Cancels a pending deletion countdown. The scheduled date/actor are retained (not cleared) so
    /// the deletion queue can show the history of what was scheduled and when it was cancelled.
    /// </summary>
    public Result CancelScheduledDeletion(DateTimeOffset now)
    {
        if (!HasPendingDeletion)
            return Result.Failure(Error.Validation("This company does not have a pending deletion to cancel."));

        DeletionCancelledAt = now;
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
    public Result ExecuteDeletion(DateTimeOffset now)
    {
        if (!HasPendingDeletion)
        {
            return Result.Failure(Error.Validation(
                "This company does not have a pending deletion to execute."));
        }

        DeletionExecutedAt = now;
        AdminForcedReadOnly = true;
        UpdatedAt = now;
        return Result.Success();
    }
}
