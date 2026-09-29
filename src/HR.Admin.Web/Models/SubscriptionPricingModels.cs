namespace HR.Admin.Web.Models;

public sealed record SubscriptionPricingConfigModel(
    List<SubscriptionPricingBandModel> Bands,
    decimal MinimumMonthlyChargeGbp);

public sealed record SubscriptionPricingBandModel(
    int StartEmployee,
    int? EndEmployee,
    decimal PricePerEmployee);

public sealed record UpdateSubscriptionPricingConfigRequest(
    List<SubscriptionPricingBandModel> Bands,
    decimal MinimumMonthlyChargeGbp);

public sealed record UpdateSubscriptionPricingConfigResult(
    SubscriptionPricingConfigModel? Config,
    IReadOnlyList<string>? Errors);
