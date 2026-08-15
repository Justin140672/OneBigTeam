namespace HR.Admin.Web.Models;

/// <summary>Mirrors GetPlatformSettingsResponse / UpdatePlatformSettingsRequest+Response 1:1.</summary>
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

/// <summary>
/// Result of a PUT attempt: either the updated settings on success, or a set of validation
/// error messages (from a 422 FluentValidation failure) to surface inline as a banner.
/// </summary>
public sealed record UpdatePlatformSettingsResult(
    PlatformSettingsModel? Settings,
    IReadOnlyList<string>? Errors);
