namespace HR.Modules.Companies.Features.GetSubscriptionPricingConfig;

// FastEndpoints' RequestBinder<T> requires at least one publicly accessible property (see
// GetPlatformSettingsRequest for the full explanation) — this endpoint takes no real input.
internal sealed class GetSubscriptionPricingConfigRequest
{
    public bool Unused { get; init; }
}
