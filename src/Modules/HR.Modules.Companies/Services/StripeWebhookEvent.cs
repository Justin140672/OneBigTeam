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
    string? PriceId,
    // OBT-REM-07: Stripe's own event id (evt_...) and creation timestamp, used for idempotency
    // (unique event id) and ordering (ignore an event older than the last one applied).
    string? EventId = null,
    DateTimeOffset? EventCreatedAt = null);
