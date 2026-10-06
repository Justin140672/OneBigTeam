using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.RevokeSupportSession;

internal sealed class RevokeSupportSessionHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<RevokeSupportSessionResponse>> HandleAsync(
        RevokeSupportSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            await PublishAttemptAsync(request.SupportSessionId, null, "unauthorized");
            return Result.Failure<RevokeSupportSessionResponse>(
                Error.Unauthorized("This account is not authorised to manage customer support sessions."));
        }

        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, currentUser.UserId ?? Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, RevokeSupportSessionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RevokeSupportSessionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var supportSession = await dbContext.SupportSessions
            .SingleOrDefaultAsync(s => s.Id == request.SupportSessionId, cancellationToken);

        if (supportSession is null)
        {
            await PublishAttemptAsync(request.SupportSessionId, null, "not_found");
            return Result.Failure<RevokeSupportSessionResponse>(
                Error.NotFound($"No support session was found with id '{request.SupportSessionId}'."));
        }

        var now = clock.UtcNowOffset();
        var expectedVersion = supportSession.Version;
        var revokeResult = supportSession.Revoke(now);
        if (revokeResult.IsFailure)
        {
            if (request.IdempotencyKey is { } racedKey)
            {
                var winner = await dbContext.TryReplayAsync<IdempotencyRecord, RevokeSupportSessionResponse>(
                    scope, racedKey, fingerprint!, cancellationToken);
                if (winner?.Kind == IdempotencyOutcomeKind.Replayed)
                {
                    return Result.Success(winner.Response!);
                }
            }

            await PublishAttemptAsync(supportSession.Id, supportSession.CompanyId, "already_revoked");
            return Result.Failure<RevokeSupportSessionResponse>(revokeResult.Error);
        }

        var response = new RevokeSupportSessionResponse(supportSession.Id, supportSession.RevokedAt!.Value);
        const string concurrencyMessage = "This support session was changed by another request. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, SupportSession, RevokeSupportSessionResponse>(
                dbContext.IdempotencyRecords, supportSession, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RevokeSupportSessionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    var winner = await dbContext.TryReplayAsync<IdempotencyRecord, RevokeSupportSessionResponse>(
                        scope, key, fingerprint!, cancellationToken);
                    if (winner?.Kind == IdempotencyOutcomeKind.Replayed)
                    {
                        return Result.Success(winner.Response!);
                    }

                    await PublishAttemptAsync(supportSession.Id, supportSession.CompanyId, "concurrency_conflict");
                    return Result.Failure<RevokeSupportSessionResponse>(Error.Concurrency(concurrencyMessage));
            }
        }
        else
        {
            var saveResult = await dbContext.SaveChangesWithConcurrencyAsync(
                supportSession, expectedVersion, concurrencyMessage, cancellationToken);
            if (saveResult.IsFailure)
            {
                await PublishAttemptAsync(supportSession.Id, supportSession.CompanyId, "concurrency_conflict");
                return Result.Failure<RevokeSupportSessionResponse>(saveResult.Error);
            }
        }

        await auditEventPublisher.PublishAsync(
            new SupportSessionRevokedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                currentUser.UserId,
                now),
            cancellationToken);

        return Result.Success(response);
    }

    private Task PublishAttemptAsync(Guid supportSessionId, Guid? companyId, string outcome) =>
        auditEventPublisher.PublishAsync(
            new SupportSessionRevocationRejectedAuditEvent(
                companyId ?? Guid.Empty,
                supportSessionId,
                currentUser.UserId,
                outcome,
                clock.UtcNowOffset()),
            CancellationToken.None);

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
