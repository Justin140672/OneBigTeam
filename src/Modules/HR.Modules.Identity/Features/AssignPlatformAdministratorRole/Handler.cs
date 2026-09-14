using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed class AssignPlatformAdministratorRoleHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AssignPlatformAdministratorRoleResponse>> HandleAsync(
        AssignPlatformAdministratorRoleRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, AssignPlatformAdministratorRoleResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AssignPlatformAdministratorRoleResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<AssignPlatformAdministratorRoleResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<AssignPlatformAdministratorRoleResponse>(Error.NotFound("Platform administrator was not found."));

        var beforeRole = administrator.Role;
        administrator.AssignRole(request.Role);

        var now = clock.UtcNow;
        var response = new AssignPlatformAdministratorRoleResponse(administrator.Id, administrator.Role);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, AssignPlatformAdministratorRoleResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorRoleAssignedAuditEvent(
                administrator.Id, administrator.Email, beforeRole, administrator.Role, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
