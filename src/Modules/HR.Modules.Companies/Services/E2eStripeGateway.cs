namespace HR.Modules.Companies.Services;

// Mirrors HR.Modules.Identity.Services.FakeSupabaseAuthGateway's rationale/DI-swap pattern exactly
// (see CompaniesModule.AddCompaniesModule's E2E_TESTING branch) — E2E tests run the real,
// Aspire-hosted app rather than a WebApplicationFactory, so there is no in-memory DI seam to swap
// in a per-test double the way tests/HR.Integration.Tests/Infrastructure/FakeStripeGateway.cs does
// for integration tests. Before this existed, IStripeGateway was unconditionally the real
// Stripe-backed gateway even under E2E_TESTING, so any E2E-reachable code path that actually calls
// Stripe (e.g. GetCustomerBillingHistoryHandler listing invoices for a seeded company's
// "dev-stub-customer" — never a real Stripe customer) made a genuine outbound call to Stripe's API,
// which could hang or fail unpredictably depending on the test environment's network reachability
// and Stripe's response to a nonexistent customer id.
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
}
