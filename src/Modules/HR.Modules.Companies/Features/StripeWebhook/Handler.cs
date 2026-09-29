using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Companies.Features.StripeWebhook;

internal sealed class StripeWebhookHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    IClock clock,
    ILogger<StripeWebhookHandler> logger)
{
    private const int MaxConcurrencyAttempts = 5;

    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        var webhookEvent = stripeGateway.ConstructAndParseWebhookEvent(payload, signatureHeader);
        var now = clock.UtcNowOffset();

        if (!string.IsNullOrWhiteSpace(webhookEvent.EventId))
        {
            var alreadyProcessed = await dbContext.ProcessedStripeEvents
                .AnyAsync(e => e.StripeEventId == webhookEvent.EventId, cancellationToken);

            if (alreadyProcessed)
            {
                logger.LogInformation(
                    "Stripe webhook {EventType} ({StripeEventId}) already processed — ignoring duplicate delivery",
                    webhookEvent.EventType, webhookEvent.EventId);
                return;
            }
        }

        if (webhookEvent.EventType is not (
            "checkout.session.completed" or
            "customer.subscription.updated" or
            "customer.subscription.deleted" or
            "customer.subscription.paused" or
            "customer.subscription.resumed"))
        {
            return;
        }

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            dbContext.ChangeTracker.Clear();

            var subscription = await FindSubscriptionAsync(webhookEvent, cancellationToken);

            if (subscription is null)
            {
                logger.LogWarning(
                    "Stripe webhook {EventType} received but no matching customer_subscriptions row was found (StripeCustomerId={StripeCustomerId}, StripeSubscriptionId={StripeSubscriptionId}, CompanyId={CompanyId})",
                    webhookEvent.EventType,
                    webhookEvent.StripeCustomerId,
                    webhookEvent.StripeSubscriptionId,
                    webhookEvent.CompanyId);
                return;
            }

            // Ticket 9 (P2): two different events sharing the same creation timestamp have no
            // reliable chronological ordering signal between them — reconcile against Stripe's own
            // current state instead of falling through to IsStaleStripeEvent's arbitrary event-id
            // CompareOrdinal tie-break, which could let a thinner "checkout.session.completed"
            // delivery incorrectly overwrite (or be incorrectly discarded in favour of) richer
            // already-applied subscription data purely by chance of id ordering. This check must run
            // BEFORE IsStaleStripeEvent below, since IsStaleStripeEvent's own tie-break would
            // otherwise resolve (and wrongly discard as "stale") exactly the same ambiguous case.
            if (subscription.IsAmbiguousWithLastApplied(webhookEvent.EventId, webhookEvent.EventCreatedAt))
            {
                var reconciled = await TryReconcileFromLiveStripeStateAsync(
                    webhookEvent, subscription, now, cancellationToken);

                if (!reconciled)
                {
                    // Deliberately NOT marking this event processed and NOT swallowing the failure —
                    // Stripe will redeliver on a non-2xx response, and the next delivery gets another
                    // chance to reconcile once whatever transient Stripe API issue clears.
                    throw new InvalidOperationException(
                        $"Could not reconcile ambiguous Stripe event {webhookEvent.EventId} against live subscription state.");
                }

                MarkProcessed(webhookEvent, subscription, applied: true, now);

                if (await TrySaveAsync(cancellationToken, attempt))
                    return;

                continue;
            }

            if (subscription.IsStaleStripeEvent(webhookEvent.EventId, webhookEvent.EventCreatedAt))
            {
                logger.LogWarning(
                    "Stripe webhook {EventType} ({StripeEventId}) is older than (or loses the deterministic tie-break against) the event already applied to subscription {StripeSubscriptionId} — ignoring out-of-order delivery",
                    webhookEvent.EventType, webhookEvent.EventId, subscription.StripeSubscriptionId);

                MarkProcessed(webhookEvent, subscription, applied: false, now);

                if (await TrySaveAsync(cancellationToken, attempt))
                    return;

                continue;
            }

            // Ticket 25 (P1): a dedicated "resumed" event must not blindly trust its own payload as
            // grounds to restore active/paid access — reconcile against the live Stripe subscription
            // first (same authoritative-fetch pattern as the ambiguous-event reconciliation above),
            // so a stale/racing "resumed" delivery can never grant access the live subscription no
            // longer actually has.
            if (webhookEvent.EventType == "customer.subscription.resumed")
            {
                var reconciled = await TryReconcileFromLiveStripeStateAsync(
                    webhookEvent, subscription, now, cancellationToken, isResumedReconciliation: true);

                if (!reconciled)
                {
                    throw new InvalidOperationException(
                        $"Could not reconcile resumed Stripe event {webhookEvent.EventId} against live subscription state.");
                }
            }
            else
            {
                ApplyProjection(webhookEvent, subscription, now);
            }

            MarkProcessed(webhookEvent, subscription, applied: true, now);

            if (await TrySaveAsync(cancellationToken, attempt))
                return;

        }

        logger.LogError(
            "Stripe webhook {EventType} ({StripeEventId}) exhausted {MaxAttempts} concurrency retry attempts without committing — Stripe will redeliver on a non-2xx response",
            webhookEvent.EventType, webhookEvent.EventId, MaxConcurrencyAttempts);

        throw new DbUpdateConcurrencyException(
            $"Could not apply Stripe event {webhookEvent.EventId} after {MaxConcurrencyAttempts} attempts due to repeated concurrent writes.");
    }

    /// <summary>
    /// Ticket 9 (P2): fetches the live Stripe subscription and applies IT (not either ambiguous
    /// webhook payload) as the projection, still recording this event's id/timestamp as the applied
    /// marker so future ordering checks are consistent. Returns false (and applies nothing) if the
    /// subscription id cannot be resolved or the live fetch fails/returns nothing — the caller
    /// treats that as retryable rather than silently accepting a stale/incorrect projection.
    /// </summary>
    private async Task<bool> TryReconcileFromLiveStripeStateAsync(
        StripeWebhookEvent webhookEvent,
        CustomerSubscription subscription,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool isResumedReconciliation = false)
    {
        var stripeSubscriptionId = webhookEvent.StripeSubscriptionId ?? subscription.StripeSubscriptionId;

        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            if (isResumedReconciliation)
            {
                return false;
            }

            ApplyProjection(webhookEvent, subscription, now);
            return true;
        }

        StripeSubscriptionSnapshot? snapshot;
        try
        {
            snapshot = await stripeGateway.GetSubscriptionAsync(stripeSubscriptionId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Stripe webhook reconciliation: failed to fetch live subscription {StripeSubscriptionId} for ambiguous event {StripeEventId}",
                stripeSubscriptionId, webhookEvent.EventId);
            return false;
        }

        if (snapshot is null)
        {
            logger.LogWarning(
                "Stripe webhook reconciliation: subscription {StripeSubscriptionId} not found in Stripe for ambiguous event {StripeEventId}",
                stripeSubscriptionId, webhookEvent.EventId);
            return false;
        }

        if (webhookEvent.EventType == "customer.subscription.deleted" && snapshot.Status == "canceled")
        {
            subscription.UpdateFromStripe(
                SubscriptionStatus.Canceled, snapshot.CurrentPeriodEnd, cancelAtPeriodEnd: true,
                now, webhookEvent.EventId, webhookEvent.EventCreatedAt);
        }
        else if (subscription.StripeCustomerId is null || subscription.StripeSubscriptionId is null)
        {
            subscription.ActivateSubscription(
                snapshot.StripeCustomerId, snapshot.StripeSubscriptionId,
                snapshot.PriceId ?? subscription.PriceId ?? string.Empty,
                snapshot.CurrentPeriodEnd, now, webhookEvent.EventId, webhookEvent.EventCreatedAt);
        }
        else
        {
            subscription.UpdateFromStripe(
                MapStatus(snapshot.Status), snapshot.CurrentPeriodEnd, snapshot.CancelAtPeriodEnd,
                now, webhookEvent.EventId, webhookEvent.EventCreatedAt);
        }

        return true;
    }

    private void ApplyProjection(StripeWebhookEvent webhookEvent, CustomerSubscription subscription, DateTimeOffset now)
    {
        switch (webhookEvent.EventType)
        {
            case "checkout.session.completed":
                subscription.ActivateSubscription(
                    webhookEvent.StripeCustomerId!,
                    webhookEvent.StripeSubscriptionId!,
                    webhookEvent.PriceId ?? subscription.PriceId ?? string.Empty,
                    webhookEvent.CurrentPeriodEnd,
                    now,
                    webhookEvent.EventId,
                    webhookEvent.EventCreatedAt);
                break;

            case "customer.subscription.updated":
                subscription.UpdateFromStripe(
                    MapStatus(webhookEvent.StripeStatus),
                    webhookEvent.CurrentPeriodEnd,
                    webhookEvent.CancelAtPeriodEnd ?? false,
                    now,
                    webhookEvent.EventId,
                    webhookEvent.EventCreatedAt);
                break;

            case "customer.subscription.deleted":
                subscription.UpdateFromStripe(
                    SubscriptionStatus.Canceled,
                    webhookEvent.CurrentPeriodEnd,
                    cancelAtPeriodEnd: true,
                    now,
                    webhookEvent.EventId,
                    webhookEvent.EventCreatedAt);
                break;

            // Ticket 25 (P1): dedicated "paused" event — always trusted directly from the payload
            // (unlike "resumed" above, moving TO the read-only Paused status carries no access-
            // granting risk that would require a live-Stripe reconciliation round trip first).
            case "customer.subscription.paused":
                subscription.UpdateFromStripe(
                    SubscriptionStatus.Paused,
                    webhookEvent.CurrentPeriodEnd,
                    webhookEvent.CancelAtPeriodEnd ?? subscription.CancelAtPeriodEnd,
                    now,
                    webhookEvent.EventId,
                    webhookEvent.EventCreatedAt);
                break;
        }
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken, int attempt)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "Stripe webhook processing lost an optimistic-concurrency race on attempt {Attempt} — reloading and re-evaluating",
                attempt);
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("ix_processed_stripe_events_stripe_event_id", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) == true)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogInformation("Stripe webhook duplicate resolved by unique constraint — no-op");
            return true;
        }
    }

    private void MarkProcessed(
        StripeWebhookEvent webhookEvent, CustomerSubscription subscription, bool applied, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(webhookEvent.EventId))
            return;

        dbContext.ProcessedStripeEvents.Add(ProcessedStripeEvent.Record(
            webhookEvent.EventId,
            webhookEvent.EventType,
            webhookEvent.EventCreatedAt ?? now,
            webhookEvent.CompanyId ?? subscription.CompanyId,
            webhookEvent.StripeSubscriptionId ?? subscription.StripeSubscriptionId,
            applied,
            now));
    }

    private async Task<CustomerSubscription?> FindSubscriptionAsync(
        StripeWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(webhookEvent.StripeCustomerId))
        {
            var byCustomerId = await dbContext.CustomerSubscriptions
                .SingleOrDefaultAsync(s => s.StripeCustomerId == webhookEvent.StripeCustomerId, cancellationToken);

            if (byCustomerId is not null)
                return byCustomerId;
        }

        if (!string.IsNullOrWhiteSpace(webhookEvent.StripeSubscriptionId))
        {
            var bySubscriptionId = await dbContext.CustomerSubscriptions
                .SingleOrDefaultAsync(s => s.StripeSubscriptionId == webhookEvent.StripeSubscriptionId, cancellationToken);

            if (bySubscriptionId is not null)
                return bySubscriptionId;
        }

        if (webhookEvent.CompanyId is Guid companyId)
        {
            return await dbContext.CustomerSubscriptions
                .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);
        }

        return null;
    }

    // Ticket 22 / Ticket 25 (P1): fail closed on any Stripe subscription status we don't
    // explicitly recognise. Previously an unmapped status silently fell through to
    // SubscriptionStatus.Active — granting full paid access without ever having confirmed that is
    // the correct projection. "paused" was originally used as this fallback's own example of an
    // "unknown" value, but it is actually a valid, documented Stripe status (see the explicit case
    // above) — it must converge to the read-only Paused status, not throw forever. Any FUTURE
    // status this switch still doesn't recognise continues to throw here: the event is NOT marked
    // processed and NOT applied (see HandleAsync/TryReconcileFromLiveStripeStateAsync, both of
    // which propagate this out uncaught), so Stripe redelivers the webhook until this is fixed,
    // rather than us guessing a status that grants access.
    private SubscriptionStatus MapStatus(string? stripeStatus)
    {
        switch (stripeStatus)
        {
            case "active" or "trialing":
                return SubscriptionStatus.Active;
            case "past_due" or "unpaid" or "incomplete":
                return SubscriptionStatus.PastDue;
            case "canceled" or "incomplete_expired":
                return SubscriptionStatus.Canceled;
            // Ticket 25 (P1): "paused" is a valid, documented Stripe subscription status (pause
            // collection) — explicitly mapped to the read-only Paused status rather than treated as
            // unrecognized. See SubscriptionStatus.Paused remarks for the product decision.
            case "paused":
                return SubscriptionStatus.Paused;
            default:
                logger.LogError(
                    "Stripe webhook received unrecognized subscription status {StripeStatus} — refusing to apply a guessed projection",
                    stripeStatus);
                throw new InvalidOperationException(
                    $"Unrecognized Stripe subscription status '{stripeStatus}' — refusing to guess a projection.");
        }
    }
}
