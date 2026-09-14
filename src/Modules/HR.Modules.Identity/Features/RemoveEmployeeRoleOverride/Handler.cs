using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;

internal sealed class RemoveEmployeeRoleOverrideHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard,
    IAuthorizationService authorizationService)
{
    public async Task<Result<RemoveEmployeeRoleOverrideResponse>> HandleAsync(
        RemoveEmployeeRoleOverrideRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RemoveEmployeeRoleOverrideResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RemoveEmployeeRoleOverrideResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<RemoveEmployeeRoleOverrideResponse>(Error.NotFound("User was not found."));

        var existing = await db.EmployeeRoleOverrides
            .FirstOrDefaultAsync(o => o.UserId == request.UserId && o.RoleId == request.RoleId, cancellationToken);

        if (existing is null)
            return Result.Failure<RemoveEmployeeRoleOverrideResponse>(Error.NotFound("Override was not found."));

        // Same role-administration boundary as creating an override — removing a Grant/Deny for a
        // role the actor is not authorised to administer is just as much a boundary violation as
        // creating one (e.g. an HR Administrator silently clearing a Company-Administrator-scoped
        // deny would circumvent the same protection from the other direction).
        var actorEffectiveRoles = actorUserId.HasValue
            ? await authorizationService.GetEffectiveRolesAsync(actorUserId.Value, cancellationToken)
            : new HashSet<Guid>();
        var administrableRoles = RoleAdministrationPolicy.GetAdministrableRoles(actorEffectiveRoles);

        if (!administrableRoles.Contains(request.RoleId))
            return Result.Failure<RemoveEmployeeRoleOverrideResponse>(
                Error.Forbidden("You are not authorised to remove this override."));

        db.EmployeeRoleOverrides.Remove(existing);

        var now = clock.UtcNowOffset();
        var response = new RemoveEmployeeRoleOverrideResponse(request.UserId, request.RoleId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, RemoveEmployeeRoleOverrideResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new EmployeeRoleOverrideRemovedAuditEvent(
                request.CompanyId,
                request.UserId,
                existing.Id,
                request.RoleId,
                existing.OverrideType,
                actorUserId,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
