using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.CancelInvite;

internal sealed class CancelInviteHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CancelInviteResponse>> HandleAsync(
        CancelInviteRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CancelInviteResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CancelInviteResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Id == request.InviteId && i.CompanyId == request.CompanyId, cancellationToken);

        if (invite is null)
            return Result.Failure<CancelInviteResponse>(Error.NotFound("Invitation was not found."));

        if (invite.IsClaimed || invite.IsCancelled)
            return Result.Failure<CancelInviteResponse>(
                Error.Conflict("Only a pending, non-cancelled invitation can be cancelled."));

        // Ticket 2 (P1): pin the version we just read so a concurrent AcceptInvite acting on stale
        // state cannot silently win the race — see AcceptInvite/Endpoint.cs's matching guard.
        var expectedVersion = invite.Version;
        var now = clock.UtcNow;
        invite.Cancel(now);

        var response = new CancelInviteResponse(invite.Id);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CancelInviteResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                invite, expectedVersion, "This invitation was already accepted and can no longer be cancelled.", cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<CancelInviteResponse>(saveResult.Error);
        }

        await auditEventPublisher.PublishAsync(
            new UserInviteCancelledAuditEvent(invite.CompanyId, invite.EmployeeId, invite.Id, invite.Email, actorUserId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
