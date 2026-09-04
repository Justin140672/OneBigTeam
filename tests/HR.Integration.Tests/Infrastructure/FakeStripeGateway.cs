using HR.Modules.Companies.Services;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Test double for the Companies module's internal IStripeGateway, registered as a singleton
/// against the shared integration test host (see ApiWebApplicationFactory). Never calls real
/// Stripe. Safe to mutate per-test because the assembly disables test parallelization
/// (see AssemblyInfo.cs), so tests run strictly sequentially against the shared factory.
/// </summary>
internal sealed class FakeStripeGateway : IStripeGateway
{
    public string CheckoutUrlToReturn { get; set; } = "https://checkout.stripe.com/test-session";

    public StripeWebhookEvent? WebhookEventToReturn { get; set; }

    public Exception? ExceptionToThrowOnConstructEvent { get; set; }

    /// <summary>
    /// OBT-REM-09: for tests that need two DIFFERENT events delivered concurrently (e.g. via
    /// Task.WhenAll against two HttpClient requests), <see cref="WebhookEventToReturn"/> alone
    /// cannot distinguish between the two in-flight requests since it is a single shared field.
    /// Callers that need distinct events per request can post a distinct raw payload string per
    /// request and register the matching event here; ConstructAndParseWebhookEvent looks the
    /// payload up in this map first and falls back to WebhookEventToReturn, so existing
    /// single-event tests are unaffected.
    /// </summary>
    public Dictionary<string, StripeWebhookEvent> WebhookEventsByPayload { get; } = [];

    public Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CheckoutUrlToReturn);
    }

    public StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader)
    {
        if (ExceptionToThrowOnConstructEvent is not null)
            throw ExceptionToThrowOnConstructEvent;

        if (WebhookEventsByPayload.TryGetValue(payload, out var keyedEvent))
            return keyedEvent;

        return WebhookEventToReturn
            ?? throw new InvalidOperationException("WebhookEventToReturn was not configured for this test.");
    }

    public string PortalUrlToReturn { get; set; } = "https://billing.stripe.com/test-portal";

    public string? LastCancelledStripeSubscriptionId { get; private set; }

    public bool? LastCancelAtPeriodEnd { get; private set; }

    public string? LastResumedStripeSubscriptionId { get; private set; }

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

    public Dictionary<string, StripeInvoiceSummary?> MostRecentPaidInvoiceByStripeCustomerId { get; set; } = [];

    public List<string> GetMostRecentPaidInvoiceCalls { get; } = [];

    public Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(FailedInvoicesToReturn);
    }

    public Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(string stripeCustomerId, CancellationToken cancellationToken)
    {
        GetMostRecentPaidInvoiceCalls.Add(stripeCustomerId);
        MostRecentPaidInvoiceByStripeCustomerId.TryGetValue(stripeCustomerId, out var invoice);
        return Task.FromResult(invoice);
    }

    public void Reset()
    {
        CheckoutUrlToReturn = "https://checkout.stripe.com/test-session";
        WebhookEventToReturn = null;
        ExceptionToThrowOnConstructEvent = null;
        WebhookEventsByPayload.Clear();
        PortalUrlToReturn = "https://billing.stripe.com/test-portal";
        LastCancelledStripeSubscriptionId = null;
        LastCancelAtPeriodEnd = null;
        LastResumedStripeSubscriptionId = null;
        LastBillingPortalStripeCustomerId = null;
        InvoicesToReturn = [];
        LastListInvoicesStripeCustomerId = null;
        FailedInvoicesToReturn = [];
        MostRecentPaidInvoiceByStripeCustomerId = [];
        GetMostRecentPaidInvoiceCalls.Clear();
    }
}
