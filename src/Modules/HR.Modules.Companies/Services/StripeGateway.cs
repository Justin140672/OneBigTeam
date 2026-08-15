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

    public async Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new InvoiceService();

        var invoices = await service.ListAsync(
            new InvoiceListOptions
            {
                Customer = stripeCustomerId,
                Limit = 100,
            },
            requestOptions,
            cancellationToken);

        return invoices.Data
            .Select(invoice => new StripeInvoiceSummary(
                invoice.Id,
                new DateTimeOffset(invoice.Created, TimeSpan.Zero),
                // Stripe amounts are in the smallest currency unit (e.g. pence for GBP).
                invoice.AmountPaid > 0 ? invoice.AmountPaid / 100m : invoice.AmountDue / 100m,
                invoice.Currency,
                invoice.Status ?? "unknown",
                invoice.StatusTransitions?.PaidAt is { } paidAt
                    ? new DateTimeOffset(paidAt, TimeSpan.Zero)
                    : null,
                invoice.HostedInvoiceUrl))
            .ToList();
    }

    public async Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new InvoiceService();

        var results = new List<FailedInvoiceSummary>();

        // Two account-wide queries (open, uncollectible) rather than per-customer iteration — see
        // IStripeGateway.ListFailedInvoicesAsync remarks. StreamAutoPagingAsync transparently
        // follows Stripe's cursor pagination so this stays correct beyond a single page (Limit is
        // just the page size, not a cap).
        foreach (var status in new[] { "open", "uncollectible" })
        {
            var listOptions = new InvoiceListOptions
            {
                Status = status,
                Limit = 100,
            };

            await foreach (var invoice in service.ListAutoPagingAsync(listOptions, requestOptions, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(invoice.CustomerId))
                    continue;

                results.Add(new FailedInvoiceSummary(
                    invoice.Id,
                    invoice.CustomerId,
                    new DateTimeOffset(invoice.Created, TimeSpan.Zero),
                    invoice.AmountRemaining / 100m,
                    invoice.Currency,
                    invoice.Status ?? "unknown",
                    invoice.NextPaymentAttempt.HasValue
                        ? new DateTimeOffset(invoice.NextPaymentAttempt.Value, TimeSpan.Zero)
                        : null,
                    invoice.HostedInvoiceUrl));
            }
        }

        return results;
    }

    public async Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions { ApiKey = options.Value.SecretKey };
        var service = new InvoiceService();

        var invoices = await service.ListAsync(
            new InvoiceListOptions
            {
                Customer = stripeCustomerId,
                Status = "paid",
                Limit = 1,
            },
            requestOptions,
            cancellationToken);

        var invoice = invoices.Data.FirstOrDefault();
        if (invoice is null)
            return null;

        return new StripeInvoiceSummary(
            invoice.Id,
            new DateTimeOffset(invoice.Created, TimeSpan.Zero),
            invoice.AmountPaid / 100m,
            invoice.Currency,
            invoice.Status ?? "unknown",
            invoice.StatusTransitions?.PaidAt is { } paidAt
                ? new DateTimeOffset(paidAt, TimeSpan.Zero)
                : null,
            invoice.HostedInvoiceUrl);
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
