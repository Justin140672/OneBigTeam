using HR.Modules.Companies.Services;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IStripeGateway"/> — never calls real Stripe. Lets handler tests
/// control the checkout URL returned and the webhook event parsed from a raw payload.
/// </summary>
internal sealed class FakeStripeGateway : IStripeGateway
{
    public string CheckoutUrlToReturn { get; set; } = "https://checkout.stripe.com/test-session";

    public StripeWebhookEvent? WebhookEventToReturn { get; set; }

    public Exception? ExceptionToThrowOnConstructEvent { get; set; }

    public Guid? LastCompanyId { get; private set; }

    public string? LastCustomerEmail { get; private set; }

    public string? LastExistingStripeCustomerId { get; private set; }

    public Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastCustomerEmail = customerEmail;
        LastExistingStripeCustomerId = existingStripeCustomerId;

        return Task.FromResult(CheckoutUrlToReturn);
    }

    public StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader)
    {
        if (ExceptionToThrowOnConstructEvent is not null)
            throw ExceptionToThrowOnConstructEvent;

        return WebhookEventToReturn
            ?? throw new InvalidOperationException("WebhookEventToReturn was not configured for this test.");
    }

    public string? LastCancelledStripeSubscriptionId { get; private set; }

    public bool? LastCancelAtPeriodEnd { get; private set; }

    public string? LastResumedStripeSubscriptionId { get; private set; }

    public string PortalUrlToReturn { get; set; } = "https://billing.stripe.com/test-portal";

    public string? LastBillingPortalStripeCustomerId { get; private set; }

    public Task CancelSubscriptionAsync(string stripeSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken)
    {
        LastCancelledStripeSubscriptionId = stripeSubscriptionId;
        LastCancelAtPeriodEnd = atPeriodEnd;
        return Task.CompletedTask;
    }

    public Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        LastResumedStripeSubscriptionId = stripeSubscriptionId;
        return Task.CompletedTask;
    }

    public Task<string> CreateBillingPortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken)
    {
        LastBillingPortalStripeCustomerId = stripeCustomerId;
        return Task.FromResult(PortalUrlToReturn);
    }

    public IReadOnlyList<StripeInvoiceSummary> InvoicesToReturn { get; set; } = [];

    public string? LastListInvoicesStripeCustomerId { get; private set; }

    public Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(string stripeCustomerId, CancellationToken cancellationToken)
    {
        LastListInvoicesStripeCustomerId = stripeCustomerId;
        return Task.FromResult(InvoicesToReturn);
    }

    public IReadOnlyList<FailedInvoiceSummary> FailedInvoicesToReturn { get; set; } = [];

    public Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(FailedInvoicesToReturn);
    }

    public IDictionary<string, StripeInvoiceSummary?> MostRecentPaidInvoiceByStripeCustomerId { get; set; } =
        new Dictionary<string, StripeInvoiceSummary?>();

    public List<string> GetMostRecentPaidInvoiceCalls { get; } = [];

    public Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(string stripeCustomerId, CancellationToken cancellationToken)
    {
        GetMostRecentPaidInvoiceCalls.Add(stripeCustomerId);

        return Task.FromResult(
            MostRecentPaidInvoiceByStripeCustomerId.TryGetValue(stripeCustomerId, out var invoice)
                ? invoice
                : null);
    }
}
