namespace HR.Modules.Companies.Features.GetPublicSubscriptionPricing;

internal sealed record GetPublicSubscriptionPricingResponse(
    IReadOnlyList<PublicSubscriptionPricingBandDto> Bands,
    decimal MinimumMonthlyChargeGbp);

internal sealed record PublicSubscriptionPricingBandDto(
    int StartEmployee,
    int? EndEmployee,
    decimal PricePerEmployee);
