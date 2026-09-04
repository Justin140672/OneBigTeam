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
            "customer.subscription.deleted"))
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

            ApplyProjection(webhookEvent, subscription, now);

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

    private static void ApplyProjection(StripeWebhookEvent webhookEvent, CustomerSubscription subscription, DateTimeOffset now)
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

    private static SubscriptionStatus MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" or "incomplete" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Active,
    };
}
