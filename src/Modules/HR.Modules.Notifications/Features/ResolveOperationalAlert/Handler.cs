using System.Threading;
using System.Threading.Tasks;

using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed class ResolveOperationalAlertHandler(
    NotificationsDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ResolveOperationalAlertResponse>> HandleAsync(
        ResolveOperationalAlertRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.Unauthorized("The current user could not be resolved."));

        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-resolve the alert. Checked ahead of the atomic
        // insert-or-replay in SaveIdempotentAsync below, which also catches a same-key request that
        // races in concurrently.
        // ResolveOperationalAlertRequest has no CompanyId (the alert's company is only known
        // after the lookup below), so the scope uses Guid.Empty for CompanyId and the resolved
        // userId as the ActorId component.
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, userId);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ResolveOperationalAlertResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ResolveOperationalAlertResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var alert = await dbContext.AdministrativeAlerts
            .SingleOrDefaultAsync(a => a.Id == request.AlertId, cancellationToken);

        if (alert is null)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.NotFound($"Operational alert '{request.AlertId}' was not found."));

        if (alert.Status == AdministrativeAlertStatus.Resolved)
            return Result.Failure<ResolveOperationalAlertResponse>(
                Error.Conflict("This operational alert has already been resolved."));

        var now = clock.UtcNowOffset();
        alert.Resolve(userId, request.ResolutionNote, now);

        // Built from in-memory values ahead of the save, so it can double as both the response and
        // the payload persisted for an idempotency replay.
        var response = new ResolveOperationalAlertResponse(
            alert.Id,
            alert.Status.ToString(),
            alert.ResolvedAt,
            alert.ResolvedByUserId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key - this attempt's alert
            // update was rolled back along with it, so skip our own audit publish and hand back the
            // winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new AdministrativeAlertResolvedAuditEvent(
                alert.CompanyId,
                alert.Id,
                userId,
                alert.ResolutionNote,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
