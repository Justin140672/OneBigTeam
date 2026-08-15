namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed record UpdatePlatformSettingsRequest(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    Dictionary<string, bool> FeatureFlags);
