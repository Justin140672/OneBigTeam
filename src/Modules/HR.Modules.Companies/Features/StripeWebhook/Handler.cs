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
    // OBT-REM-09: bounded retry for the optimistic-concurrency race between two different Stripe
    // events for the same subscription. A handful of attempts is enough to ride out a genuine race
    // between two concurrent deliveries without risking an unbounded retry storm; Stripe itself will
    // redeliver on a webhook timeout/5xx, so exhausting retries here is not a lost event.
    private const int MaxConcurrencyAttempts = 5;

    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        // Signature verification happens inside the gateway; a bad signature throws before this line,
        // so no ProcessedStripeEvent/subscription row is ever written for an invalid signature.
        var webhookEvent = stripeGateway.ConstructAndParseWebhookEvent(payload, signatureHeader);
        var now = clock.UtcNowOffset();

        // OBT-REM-07: idempotency — a redelivery of an event we have already processed is a
        // successful no-op. (Stripe retries deliveries aggressively; without this an "updated"
        // event replayed after a later one would clobber newer state.)
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

        // Unknown/unhandled event types have no subscription side effects and are not tracked for
        // idempotency at all — nothing to project, nothing worth an ordering marker.
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

                continue; // Concurrency conflict — reload and re-evaluate.
            }

            // OBT-REM-09: ordering guard evaluated against the durable marker on the subscription row
            // itself (not a separate table query) so the "is this event newer" decision and the
            // projection write are protected by the SAME optimistic-concurrency token, in the SAME
            // transaction. An older event is recorded as processed but not applied.
            if (subscription.IsStaleStripeEvent(webhookEvent.EventId, webhookEvent.EventCreatedAt))
            {
                logger.LogWarning(
                    "Stripe webhook {EventType} ({StripeEventId}) is older than (or loses the deterministic tie-break against) the event already applied to subscription {StripeSubscriptionId} — ignoring out-of-order delivery",
                    webhookEvent.EventType, webhookEvent.EventId, subscription.StripeSubscriptionId);

                MarkProcessed(webhookEvent, subscription, applied: false, now);

                if (await TrySaveAsync(cancellationToken, attempt))
                    return;

                continue; // Concurrency conflict — reload and re-evaluate.
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

            // The processed-event row is written in the SAME SaveChanges as the projection: the event
            // is "processed" only if and when the local state change commits. A concurrent duplicate
            // delivery of the SAME event id loses the race on the unique stripe_event_id index —
            // treated as a successful no-op. A concurrent delivery of a DIFFERENT event for the same
            // subscription loses the race on the Version concurrency token instead, and retries.
            MarkProcessed(webhookEvent, subscription, applied: true, now);

            if (await TrySaveAsync(cancellationToken, attempt))
                return;

            // Lost the optimistic-concurrency race to another event for this subscription — reload
            // current state and re-evaluate from scratch. The winner's write is now visible, so this
            // event may turn out to be stale (correctly skipped) or may still need to be applied
            // (e.g. two different, non-conflicting fields) depending on what actually committed.
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
                // No subscription id to reconcile a "resumed" event against — refuse to guess and
                // let the caller treat this as retryable, rather than applying the raw payload as
                // an ambiguous tie would.
                return false;
            }

            // A bare checkout.session.completed tie with no subscription id to reconcile against —
            // fall back to applying the webhook payload directly; there is nothing more authoritative
            // to fetch.
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
            // No paid subscription linked yet — reconciling a tie can only mean the very first
            // activation (checkout.session.completed racing something else), so go through
            // ActivateSubscription to set the customer/subscription/price linkage too.
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

    /// <summary>
    /// Attempts to commit. Returns true if the commit succeeded (or lost a same-event-id duplicate
    /// race, which is also a terminal success). Returns false when the caller should reload and
    /// retry (lost the Version concurrency race against a different event for the same subscription).
    /// </summary>
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
            // Concurrent duplicate delivery of the SAME event id — the winner applied the projection
            // and recorded the event. Discard this caller's tracked changes and treat as a successful
            // no-op; this is terminal, not a retry.
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
