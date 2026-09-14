using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.EnablePlatformAdministrator;

internal sealed class EnablePlatformAdministratorHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<EnablePlatformAdministratorResponse>> HandleAsync(
        EnablePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, EnablePlatformAdministratorResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<EnablePlatformAdministratorResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<EnablePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<EnablePlatformAdministratorResponse>(Error.NotFound("Platform administrator was not found."));

        if (administrator.IsEnabled)
            return Result.Failure<EnablePlatformAdministratorResponse>(Error.Conflict("Platform administrator account is already enabled."));

        var now = clock.UtcNow;
        administrator.Enable(now);

        var response = new EnablePlatformAdministratorResponse(administrator.Id, administrator.IsEnabled);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, EnablePlatformAdministratorResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorEnabledAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
