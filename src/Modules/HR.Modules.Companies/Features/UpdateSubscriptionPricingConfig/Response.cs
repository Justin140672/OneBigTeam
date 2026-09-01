namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed record UpdateSubscriptionPricingConfigResponse(
    IReadOnlyList<UpdateSubscriptionPricingBandDto> Bands,
    decimal MinimumMonthlyChargeGbp,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

internal sealed record UpdateSubscriptionPricingBandDto(
    int StartEmployee,
    int? EndEmployee,
    decimal PricePerEmployee);
