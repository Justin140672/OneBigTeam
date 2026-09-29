namespace HR.Modules.Companies.Services;

internal sealed class E2eStripeGateway : IStripeGateway
{
    public Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken) =>
        Task.FromResult("https://checkout.stripe.com/e2e-fake-session");

    public StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader) =>
        throw new InvalidOperationException(
            "No real Stripe project is configured for E2E testing — there is nothing that can send " +
            "a genuine webhook here.");

    public Task CancelSubscriptionAsync(string stripeSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<string> CreateBillingPortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken) =>
        Task.FromResult("https://billing.stripe.com/e2e-fake-portal");

    public Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(string stripeCustomerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StripeInvoiceSummary>>([]);

    public Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FailedInvoiceSummary>>([]);

    public Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(string stripeCustomerId, CancellationToken cancellationToken) =>
        Task.FromResult<StripeInvoiceSummary?>(null);

    public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken) =>
        Task.FromResult<StripeSubscriptionSnapshot?>(null);
}
