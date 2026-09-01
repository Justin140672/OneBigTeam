namespace HR.Modules.Companies.Features.GetPublicSubscriptionPricing;

// FastEndpoints' RequestBinder<T> requires at least one publicly accessible property (see
// GetPlatformSettingsRequest for the full explanation) — this endpoint takes no real input.
internal sealed class GetPublicSubscriptionPricingRequest
{
    public bool Unused { get; init; }
}
