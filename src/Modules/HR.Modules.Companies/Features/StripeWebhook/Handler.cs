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
    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        // Signature verification happens inside the gateway; a bad signature throws before this line.
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

        var subscription = await FindSubscriptionAsync(webhookEvent, cancellationToken);

        if (subscription is null)
        {
            if (webhookEvent.EventType is
                "checkout.session.completed" or
                "customer.subscription.updated" or
                "customer.subscription.deleted")
            {
                logger.LogWarning(
                    "Stripe webhook {EventType} received but no matching customer_subscriptions row was found (StripeCustomerId={StripeCustomerId}, StripeSubscriptionId={StripeSubscriptionId}, CompanyId={CompanyId})",
                    webhookEvent.EventType,
                    webhookEvent.StripeCustomerId,
                    webhookEvent.StripeSubscriptionId,
                    webhookEvent.CompanyId);
            }

            return;
        }

        // OBT-REM-07: ordering guard for subscription lifecycle events. If we have already applied a
        // strictly newer event for this subscription, this one is stale — record it as seen but do
        // not let it overwrite newer state.
        var isSubscriptionLifecycle = webhookEvent.EventType is
            "customer.subscription.updated" or "customer.subscription.deleted";

        if (isSubscriptionLifecycle && webhookEvent.EventCreatedAt is { } createdAt)
        {
            var newerAlreadyApplied = await dbContext.ProcessedStripeEvents
                .Where(e => e.Applied
                    && e.StripeSubscriptionId != null
                    && e.StripeSubscriptionId == subscription.StripeSubscriptionId
                    && (e.EventType == "customer.subscription.updated"
                        || e.EventType == "customer.subscription.deleted"))
                .AnyAsync(e => e.EventCreatedAt >= createdAt, cancellationToken);

            if (newerAlreadyApplied)
            {
                logger.LogWarning(
                    "Stripe webhook {EventType} ({StripeEventId}) is older than an event already applied to subscription {StripeSubscriptionId} — ignoring out-of-order delivery",
                    webhookEvent.EventType, webhookEvent.EventId, subscription.StripeSubscriptionId);

                MarkProcessed(webhookEvent, subscription, applied: false, now);
                await SaveProcessedAsync(cancellationToken);
                return;
            }
        }

        switch (webhookEvent.EventType)
        {
            case "checkout.session.completed":
                subscription.ActivateSubscription(
                    webhookEvent.StripeCustomerId!,
                    webhookEvent.StripeSubscriptionId!,
                    webhookEvent.PriceId ?? subscription.PriceId ?? string.Empty,
                    webhookEvent.CurrentPeriodEnd,
                    now);
                break;

            case "customer.subscription.updated":
                subscription.UpdateFromStripe(
                    MapStatus(webhookEvent.StripeStatus),
                    webhookEvent.CurrentPeriodEnd,
                    webhookEvent.CancelAtPeriodEnd ?? false,
                    now);
                break;

            case "customer.subscription.deleted":
                subscription.UpdateFromStripe(
                    SubscriptionStatus.Canceled,
                    webhookEvent.CurrentPeriodEnd,
                    cancelAtPeriodEnd: true,
                    now);
                break;

            default:
                return;
        }

        // The processed-event row is written in the SAME SaveChanges as the projection: the event is
        // "processed" only if and when the local state change commits. A concurrent duplicate loses
        // the race on the unique stripe_event_id index — treat that as a successful no-op.
        MarkProcessed(webhookEvent, subscription, applied: true, now);
        await SaveProcessedAsync(cancellationToken);
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

    private async Task SaveProcessedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("ix_processed_stripe_events_stripe_event_id", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Concurrent duplicate delivery — the winner applied the projection and recorded the
            // event. Discard this caller's tracked changes and treat as a successful no-op.
            dbContext.ChangeTracker.Clear();
            logger.LogInformation("Stripe webhook duplicate resolved by unique constraint — no-op");
        }
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
