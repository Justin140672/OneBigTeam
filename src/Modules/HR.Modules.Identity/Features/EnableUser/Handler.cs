using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.EnableUser;

internal sealed class EnableUserHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard)
{
    public async Task<Result<EnableUserResponse>> HandleAsync(
        EnableUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, EnableUserResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<EnableUserResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // IAM-01: confirm the target user belongs to the route company before touching account status.
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<EnableUserResponse>(Error.NotFound("User was not found."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        // Ticket 1 (P1): mirror DisableUser's fallback to UserProfile for real Supabase-backed
        // accounts (AcceptInvite, self-service SignUp).
        var profile = user is null
            ? await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == request.UserId, cancellationToken)
            : null;

        if (user is null && profile is null)
            return Result.Failure<EnableUserResponse>(Error.NotFound("User was not found."));

        var isCurrentlyActive = user?.IsActive ?? profile!.IsActive;
        if (isCurrentlyActive)
            return Result.Failure<EnableUserResponse>(Error.Conflict("User account is already active."));

        var now = clock.UtcNow;
        user?.Reactivate(now);
        profile?.Reactivate(now);

        var targetId = user?.Id ?? profile!.Id;
        var response = new EnableUserResponse(targetId, user?.IsActive ?? profile!.IsActive);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, EnableUserResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new UserEnabledAuditEvent(request.CompanyId, targetId, targetId, actorUserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
