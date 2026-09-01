namespace HR.Modules.Companies.Features.GetSubscriptionPricingConfig;

internal sealed record GetSubscriptionPricingConfigResponse(
    IReadOnlyList<SubscriptionPricingBandDto> Bands,
    decimal MinimumMonthlyChargeGbp);

internal sealed record SubscriptionPricingBandDto(
    int StartEmployee,
    int? EndEmployee,
    decimal PricePerEmployee);
