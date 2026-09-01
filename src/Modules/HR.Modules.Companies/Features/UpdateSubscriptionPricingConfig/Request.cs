namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed record UpdateSubscriptionPricingConfigRequest(
    IReadOnlyList<UpdateSubscriptionPricingBandInput> Bands,
    decimal MinimumMonthlyChargeGbp);

internal sealed record UpdateSubscriptionPricingBandInput(
    int StartEmployee,
    int? EndEmployee,
    decimal PricePerEmployee);
