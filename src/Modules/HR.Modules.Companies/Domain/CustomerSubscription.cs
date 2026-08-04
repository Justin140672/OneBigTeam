using HR.Infrastructure.Abstractions;

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
}
