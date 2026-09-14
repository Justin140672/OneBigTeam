using System.Text.Json;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
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
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, UpdatePlatformSettingsResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<UpdatePlatformSettingsResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var featureFlags = JsonSerializer.Deserialize<Dictionary<string, bool>>(settings.FeatureFlagsJson) ?? [];

        var response = new UpdatePlatformSettingsResponse(
            settings.TrialLengthDays,
            settings.DefaultMonthlyPriceGbp,
            settings.SupportEmail,
            settings.MaintenanceModeEnabled,
            settings.MaintenanceModeMessage,
            featureFlags,
            settings.UpdatedAt,
            settings.UpdatedByUserId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

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

        return Result.Success(response);
    }
}
