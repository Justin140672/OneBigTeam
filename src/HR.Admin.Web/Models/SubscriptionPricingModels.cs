namespace HR.Admin.Web.Models;

/// <summary>Mirrors GetSubscriptionPricingConfigResponse / UpdateSubscriptionPricingConfigRequest (Story 4).</summary>
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

/// <summary>Either the saved config on success, or validation error messages to surface inline.</summary>
public sealed record UpdateSubscriptionPricingConfigResult(
    SubscriptionPricingConfigModel? Config,
    IReadOnlyList<string>? Errors);
