namespace HR.Modules.Companies.Features.GetPlatformSettings;

internal sealed record GetPlatformSettingsResponse(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    Dictionary<string, bool> FeatureFlags,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
