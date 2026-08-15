using System.Text.Json;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed class UpdatePlatformSettingsHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdatePlatformSettingsResponse>> HandleAsync(
        UpdatePlatformSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var settings = await dbContext.PlatformSettings
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = PlatformSettings.CreateDefault(now);
            dbContext.PlatformSettings.Add(settings);
        }

        var previousState = new PlatformSettingsAuditSnapshot(
            settings.TrialLengthDays,
            settings.DefaultMonthlyPriceGbp,
            settings.SupportEmail,
            settings.MaintenanceModeEnabled,
            settings.MaintenanceModeMessage,
            settings.FeatureFlagsJson);

        var featureFlagsJson = JsonSerializer.Serialize(request.FeatureFlags ?? []);

        var updateResult = settings.Update(
            request.TrialLengthDays,
            request.DefaultMonthlyPriceGbp,
            request.SupportEmail,
            request.MaintenanceModeEnabled,
            request.MaintenanceModeMessage,
            featureFlagsJson,
            currentUser.UserId,
            now);

        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdatePlatformSettingsResponse>(updateResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformSettingsUpdatedAuditEvent(
                settings.Id,
                currentUser.UserId,
                now,
                previousState,
                new PlatformSettingsAuditSnapshot(
                    settings.TrialLengthDays,
                    settings.DefaultMonthlyPriceGbp,
                    settings.SupportEmail,
                    settings.MaintenanceModeEnabled,
                    settings.MaintenanceModeMessage,
                    settings.FeatureFlagsJson)),
            cancellationToken);

        var featureFlags = JsonSerializer.Deserialize<Dictionary<string, bool>>(settings.FeatureFlagsJson) ?? [];

        return Result.Success(new UpdatePlatformSettingsResponse(
            settings.TrialLengthDays,
            settings.DefaultMonthlyPriceGbp,
            settings.SupportEmail,
            settings.MaintenanceModeEnabled,
            settings.MaintenanceModeMessage,
            featureFlags,
            settings.UpdatedAt,
            settings.UpdatedByUserId));
    }
}
