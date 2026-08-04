using Microsoft.Extensions.Options;

using Stripe;
using Stripe.Checkout;

namespace HR.Modules.Companies.Services;

internal sealed class StripeGateway(IOptions<StripeOptions> options) : IStripeGateway
{
    public async Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = options.Value.PriceId,
                    Quantity = 1,
                },
            ],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["companyId"] = companyId.ToString(),
            },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["companyId"] = companyId.ToString(),
                },
            },
        };

        if (string.IsNullOrWhiteSpace(existingStripeCustomerId))
        {
            sessionOptions.CustomerEmail = customerEmail;
        }
        else
        {
            sessionOptions.Customer = existingStripeCustomerId;
        }

        var service = new SessionService();
        var session = await service.CreateAsync(sessionOptions, requestOptions, cancellationToken);

        return session.Url;
    }

    public StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader)
    {
        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, options.Value.WebhookSecret);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                var session = stripeEvent.Data.Object as Session;
                var companyId = ParseCompanyId(session?.Metadata);

                return new StripeWebhookEvent(
                    stripeEvent.Type,
                    session?.CustomerId,
                    session?.SubscriptionId,
                    companyId,
                    CurrentPeriodEnd: null,
                    CancelAtPeriodEnd: null,
                    StripeStatus: null,
                    PriceId: options.Value.PriceId);
            }

            case "customer.subscription.updated":
            case "customer.subscription.deleted":
            {
                var subscription = stripeEvent.Data.Object as Subscription;
                var companyId = ParseCompanyId(subscription?.Metadata);
                var currentPeriodEnd = subscription?.Items?.Data?
                    .Select(item => item.CurrentPeriodEnd)
                    .DefaultIfEmpty()
                    .Max();

                return new StripeWebhookEvent(
                    stripeEvent.Type,
                    subscription?.CustomerId,
                    subscription?.Id,
                    companyId,
                    currentPeriodEnd,
                    subscription?.CancelAtPeriodEnd,
                    subscription?.Status,
                    PriceId: null);
            }

            default:
                return new StripeWebhookEvent(stripeEvent.Type, null, null, null, null, null, null, null);
        }
    }

    public async Task CancelSubscriptionAsync(
        string stripeSubscriptionId,
        bool atPeriodEnd,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new SubscriptionService();

        if (atPeriodEnd)
        {
            await service.UpdateAsync(
                stripeSubscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true },
                requestOptions,
                cancellationToken);
        }
        else
        {
            await service.CancelAsync(stripeSubscriptionId, null, requestOptions, cancellationToken);
        }
    }

    public async Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new SubscriptionService();

        await service.UpdateAsync(
            stripeSubscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = false },
            requestOptions,
            cancellationToken);
    }

    public async Task<string> CreateBillingPortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new Stripe.BillingPortal.SessionService();

        var session = await service.CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = stripeCustomerId,
                ReturnUrl = returnUrl,
            },
            requestOptions,
            cancellationToken);

        return session.Url;
    }

    private static Guid? ParseCompanyId(IDictionary<string, string>? metadata)
    {
        if (metadata is not null
            && metadata.TryGetValue("companyId", out var raw)
            && Guid.TryParse(raw, out var companyId))
        {
            return companyId;
        }

        return null;
    }
}
