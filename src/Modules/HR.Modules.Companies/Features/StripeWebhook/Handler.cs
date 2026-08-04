using HR.Infrastructure.Abstractions;
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
        var webhookEvent = stripeGateway.ConstructAndParseWebhookEvent(payload, signatureHeader);
        var now = clock.UtcNowOffset();

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

        await dbContext.SaveChangesAsync(cancellationToken);
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
