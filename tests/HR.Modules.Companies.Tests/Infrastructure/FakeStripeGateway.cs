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

    /// <summary>Simulates a Stripe API timeout / rejected request on the write/read calls below.</summary>
    public Exception? ExceptionToThrowOnCheckout { get; set; }

    public Exception? ExceptionToThrowOnCancel { get; set; }

    public Exception? ExceptionToThrowOnResume { get; set; }

    public Exception? ExceptionToThrowOnBillingPortal { get; set; }

    public Exception? ExceptionToThrowOnListInvoices { get; set; }

    public int CancelCallCount { get; private set; }

    public int ResumeCallCount { get; private set; }

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

        if (ExceptionToThrowOnCheckout is not null)
            return Task.FromException<string>(ExceptionToThrowOnCheckout);

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
        CancelCallCount++;
        LastCancelledStripeSubscriptionId = stripeSubscriptionId;
        LastCancelAtPeriodEnd = atPeriodEnd;
        if (ExceptionToThrowOnCancel is not null)
            return Task.FromException(ExceptionToThrowOnCancel);
        return Task.CompletedTask;
    }

    public Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        ResumeCallCount++;
        LastResumedStripeSubscriptionId = stripeSubscriptionId;
        if (ExceptionToThrowOnResume is not null)
            return Task.FromException(ExceptionToThrowOnResume);
        return Task.CompletedTask;
    }

    public Task<string> CreateBillingPortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken)
    {
        LastBillingPortalStripeCustomerId = stripeCustomerId;
        if (ExceptionToThrowOnBillingPortal is not null)
            return Task.FromException<string>(ExceptionToThrowOnBillingPortal);
        return Task.FromResult(PortalUrlToReturn);
    }

    public IReadOnlyList<StripeInvoiceSummary> InvoicesToReturn { get; set; } = [];

    public string? LastListInvoicesStripeCustomerId { get; private set; }

    public Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(string stripeCustomerId, CancellationToken cancellationToken)
    {
        LastListInvoicesStripeCustomerId = stripeCustomerId;
        if (ExceptionToThrowOnListInvoices is not null)
            return Task.FromException<IReadOnlyList<StripeInvoiceSummary>>(ExceptionToThrowOnListInvoices);
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
