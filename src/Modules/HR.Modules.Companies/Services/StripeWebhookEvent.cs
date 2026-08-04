namespace HR.Modules.Companies.Services;

/// <summary>
/// Flattened, module-internal projection of a verified Stripe webhook event. Keeps Stripe.net
/// types out of handler/endpoint code — only StripeGateway touches the SDK directly.
/// </summary>
internal sealed record StripeWebhookEvent(
    string EventType,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    Guid? CompanyId,
    DateTimeOffset? CurrentPeriodEnd,
    bool? CancelAtPeriodEnd,
    string? StripeStatus,
    string? PriceId);
