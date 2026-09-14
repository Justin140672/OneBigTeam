using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.DisablePlatformAdministrator;

internal sealed class DisablePlatformAdministratorHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<DisablePlatformAdministratorResponse>> HandleAsync(
        DisablePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, DisablePlatformAdministratorResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<DisablePlatformAdministratorResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<DisablePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<DisablePlatformAdministratorResponse>(Error.NotFound("Platform administrator was not found."));

        if (!administrator.IsEnabled)
            return Result.Failure<DisablePlatformAdministratorResponse>(Error.Conflict("Platform administrator account is already disabled."));

        var now = clock.UtcNow;
        administrator.Disable(now, currentUser.UserId);

        var response = new DisablePlatformAdministratorResponse(administrator.Id, administrator.IsEnabled);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, DisablePlatformAdministratorResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorDisabledAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
