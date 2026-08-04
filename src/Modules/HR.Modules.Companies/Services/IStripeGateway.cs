namespace HR.Modules.Companies.Services;

internal interface IStripeGateway
{
    Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken);

    StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader);

    Task CancelSubscriptionAsync(
        string stripeSubscriptionId,
        bool atPeriodEnd,
        CancellationToken cancellationToken);

    Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken);

    Task<string> CreateBillingPortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken cancellationToken);
}
