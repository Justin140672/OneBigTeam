namespace HR.Admin.Web.Models;

public sealed record PlatformSettingsModel(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    Dictionary<string, bool> FeatureFlags,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

public sealed record UpdatePlatformSettingsRequest(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    Dictionary<string, bool> FeatureFlags);

public sealed record UpdatePlatformSettingsResult(
    PlatformSettingsModel? Settings,
    IReadOnlyList<string>? Errors);
