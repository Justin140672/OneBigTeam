namespace HR.Modules.Companies.Services;

internal sealed record StripeWebhookEvent(
    string EventType,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    Guid? CompanyId,
    DateTimeOffset? CurrentPeriodEnd,
    bool? CancelAtPeriodEnd,
    string? StripeStatus,
    string? PriceId,
    string? EventId = null,
    DateTimeOffset? EventCreatedAt = null);
