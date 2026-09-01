using System.Text.Json;

using HR.SharedKernel;
using HR.SharedKernel.Pricing;

namespace HR.Modules.Companies.Domain;

/// <summary>
/// Platform-wide configuration singleton (Platform Monitoring/Admin epic) — exactly one row ever
/// exists, identified by the fixed <see cref="SingletonId"/>. This is a documented exception to
/// the tenant-owned-tables company_id rule (see specifications/architecture/05-database-standards.md
/// "Global/system tables may omit company_id"): unlike a read-only lookup table (e.g. countries),
/// this table is admin-writable — platform administrators update it via the Admin Portal to control
/// trial length, default pricing display, support contact, maintenance mode, and feature flags.
/// </summary>
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

    /// <summary>Serialized <c>Dictionary&lt;string, bool&gt;</c> of feature flag name -> enabled, stored as jsonb.</summary>
    public string FeatureFlagsJson { get; private set; } = "{}";

    /// <summary>
    /// Serialized <see cref="IReadOnlyList{T}"/> of <see cref="SubscriptionPricingBand"/>, stored as
    /// jsonb. Together with <see cref="MinimumMonthlyChargeGbp"/> this is the single authoritative
    /// configurable pricing model (Story 4) used by marketing, customer billing and the Admin app.
    /// </summary>
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

    /// <summary>Deserializes the persisted bands + minimum into a <see cref="SubscriptionPricingConfig"/>.</summary>
    public SubscriptionPricingConfig GetPricingConfig()
    {
        var bands = JsonSerializer.Deserialize<List<SubscriptionPricingBand>>(
            string.IsNullOrWhiteSpace(PricingBandsJson) ? "[]" : PricingBandsJson,
            PricingJsonOptions) ?? [];

        return bands.Count == 0
            ? SubscriptionPricingConfig.Default
            : new SubscriptionPricingConfig(bands, MinimumMonthlyChargeGbp);
    }

    /// <summary>
    /// Replaces the configurable subscription pricing model. Runs <see cref="SubscriptionPricingConfig.Validate"/>
    /// first and mutates nothing on failure.
    /// </summary>
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
