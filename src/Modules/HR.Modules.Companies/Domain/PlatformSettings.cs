using System.Text.Json;

using HR.SharedKernel;
using HR.SharedKernel.Pricing;

namespace HR.Modules.Companies.Domain;

internal sealed class PlatformSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    private PlatformSettings() { }

    public Guid Id { get; private set; }
    public int TrialLengthDays { get; private set; }
    public decimal DefaultMonthlyPriceGbp { get; private set; }
    public string SupportEmail { get; private set; } = string.Empty;
    public bool MaintenanceModeEnabled { get; private set; }
    public string? MaintenanceModeMessage { get; private set; }

    public string FeatureFlagsJson { get; private set; } = "{}";

    public string PricingBandsJson { get; private set; } = "[]";

    public decimal MinimumMonthlyChargeGbp { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private static readonly JsonSerializerOptions PricingJsonOptions = new(JsonSerializerDefaults.Web);

    public static PlatformSettings CreateDefault(DateTimeOffset now)
    {
        return new PlatformSettings
        {
            Id = SingletonId,
            TrialLengthDays = 14,
            DefaultMonthlyPriceGbp = 20.00m,
            SupportEmail = "support@hrplatform.com",
            MaintenanceModeEnabled = false,
            MaintenanceModeMessage = null,
            FeatureFlagsJson = "{}",
            PricingBandsJson = JsonSerializer.Serialize(SubscriptionPricingConfig.Default.Bands, PricingJsonOptions),
            MinimumMonthlyChargeGbp = SubscriptionPricingConfig.Default.MinimumMonthlyChargeGbp,
            UpdatedAt = now,
            UpdatedByUserId = null,
        };
    }

    public SubscriptionPricingConfig GetPricingConfig()
    {
        var bands = JsonSerializer.Deserialize<List<SubscriptionPricingBand>>(
            string.IsNullOrWhiteSpace(PricingBandsJson) ? "[]" : PricingBandsJson,
            PricingJsonOptions) ?? [];

        return bands.Count == 0
            ? SubscriptionPricingConfig.Default
            : new SubscriptionPricingConfig(bands, MinimumMonthlyChargeGbp);
    }

    public Result UpdatePricingConfig(SubscriptionPricingConfig config, Guid? updatedByUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        var validation = config.Validate();
        if (validation.IsFailure)
        {
            return validation;
        }

        PricingBandsJson = JsonSerializer.Serialize(config.Bands, PricingJsonOptions);
        MinimumMonthlyChargeGbp = config.MinimumMonthlyChargeGbp;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Update(
        int trialLengthDays,
        decimal defaultMonthlyPriceGbp,
        string supportEmail,
        bool maintenanceModeEnabled,
        string? maintenanceModeMessage,
        string featureFlagsJson,
        Guid? updatedByUserId,
        DateTimeOffset now)
    {
        if (trialLengthDays <= 0)
            return Result.Failure(Error.Validation("Trial length in days must be greater than zero."));

        if (defaultMonthlyPriceGbp < 0)
            return Result.Failure(Error.Validation("Default monthly price cannot be negative."));

        if (string.IsNullOrWhiteSpace(supportEmail))
            return Result.Failure(Error.Validation("Support email is required."));

        TrialLengthDays = trialLengthDays;
        DefaultMonthlyPriceGbp = defaultMonthlyPriceGbp;
        SupportEmail = supportEmail;
        MaintenanceModeEnabled = maintenanceModeEnabled;
        MaintenanceModeMessage = maintenanceModeMessage;
        FeatureFlagsJson = featureFlagsJson;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = now;
        return Result.Success();
    }
}
