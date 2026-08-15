using System.Text.Json;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetPlatformSettings;

/// <summary>
/// Lazy-seeds the singleton PlatformSettings row (see PlatformSettings.SingletonId remarks) on
/// first read, so the Admin Portal settings screen always has a row to display even before any
/// administrator has ever saved a change.
/// </summary>
internal sealed class GetPlatformSettingsHandler(CompaniesDbContext dbContext, IClock clock)
{
    public async Task<Result<GetPlatformSettingsResponse>> HandleAsync(
        GetPlatformSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.PlatformSettings
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = PlatformSettings.CreateDefault(clock.UtcNowOffset());
            dbContext.PlatformSettings.Add(settings);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var featureFlags = DeserializeFeatureFlags(settings.FeatureFlagsJson);

        return Result.Success(new GetPlatformSettingsResponse(
            settings.TrialLengthDays,
            settings.DefaultMonthlyPriceGbp,
            settings.SupportEmail,
            settings.MaintenanceModeEnabled,
            settings.MaintenanceModeMessage,
            featureFlags,
            settings.UpdatedAt,
            settings.UpdatedByUserId));
    }

    private static Dictionary<string, bool> DeserializeFeatureFlags(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
    }
}
