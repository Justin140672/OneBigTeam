using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.DisableUser;

internal sealed class DisableUserHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard,
    LastActiveAdministratorGuard lastActiveAdministratorGuard)
{
    public async Task<Result<DisableUserResponse>> HandleAsync(
        DisableUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, DisableUserResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<DisableUserResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<DisableUserResponse>(Error.NotFound("User was not found."));

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == request.UserId, cancellationToken);

        if (profile is null)
            return Result.Failure<DisableUserResponse>(Error.NotFound("User was not found."));

        if (!profile.IsActive)
            return Result.Failure<DisableUserResponse>(Error.Conflict("User account is already disabled."));

        var now = clock.UtcNow;

        var protectedRoleIds = (await db.UserRoles
            .Where(ur => ur.UserId == request.UserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken))
            .Where(RoleAdministrationPolicy.IsLockoutProtected)
            .ToList();

        foreach (var protectedRoleId in protectedRoleIds)
        {
            var hasOtherActiveHolder = await lastActiveAdministratorGuard.HasOtherActiveHolderAsync(
                request.CompanyId, protectedRoleId, request.UserId, cancellationToken);

            if (!hasOtherActiveHolder)
            {
                await auditEventPublisher.PublishAsync(
                    new RoleChangeRejectedAuditEvent(
                        request.CompanyId,
                        request.UserId,
                        request.UserId,
                        "last_active_administrator_disable",
                        [],
                        actorUserId,
                        now),
                    cancellationToken);

                return Result.Failure<DisableUserResponse>(
                    Error.Conflict("This user is the last active holder of a protected administrator role and cannot be disabled."));
            }
        }

        profile.Deactivate(now);

        var targetId = profile.Id;
        var response = new DisableUserResponse(targetId, profile.IsActive);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, DisableUserResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new UserDisabledAuditEvent(request.CompanyId, targetId, targetId, actorUserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
