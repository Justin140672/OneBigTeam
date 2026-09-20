using System.Security.Cryptography;
using System.Text;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed class RedeemSupportSessionHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ISupportSessionTokenIssuer tokenIssuer)
{
    public async Task<Result<RedeemSupportSessionResponse>> HandleAsync(
        RedeemSupportSessionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, RedeemSupportSessionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RedeemSupportSessionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var tokenHash = HashToken(request.Token);

        // Tracked (not AsNoTracking) — SaveChangesWithConcurrencyAsync below needs EF change
        // tracking to pin the OriginalValue of Version and detect a concurrent redeemer.
        var supportSession = await dbContext.SupportSessions
            .SingleOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);

        if (supportSession is null)
        {
            return Result.Failure<RedeemSupportSessionResponse>(
                Error.NotFound("No matching support session was found for this token."));
        }

        var now = clock.UtcNowOffset();
        var expectedVersion = supportSession.Version;

        var redeemResult = supportSession.Redeem(now);
        if (redeemResult.IsFailure)
        {
            return Result.Failure<RedeemSupportSessionResponse>(redeemResult.Error);
        }

        // P1: atomic, race-safe redemption via the shared optimistic-concurrency pattern (Ticket
        // 2 — see SupportSession.Version's remarks). Two simultaneous redemption requests for the
        // same token must never both succeed: whichever request's SaveChangesAsync commits first
        // advances Version, so the loser's pinned OriginalValue no longer matches the stored row,
        // its UPDATE affects zero rows, and EF raises DbUpdateConcurrencyException — translated
        // here to a validation failure rather than a silent double-success.
        var saveResult = await dbContext.SaveChangesWithConcurrencyAsync(
            supportSession, expectedVersion, "This support session has already been redeemed.", cancellationToken);

        if (saveResult.IsFailure)
        {
            return Result.Failure<RedeemSupportSessionResponse>(saveResult.Error);
        }

        var expiresAt = supportSession.ExpiresAt;
        var token = tokenIssuer.IssueToken(
            supportSession.Id,
            supportSession.CompanyId,
            supportSession.IssuedByAdminUserId,
            supportSession.IssuedByAdminEmail,
            expiresAt);

        var response = new RedeemSupportSessionResponse(
            supportSession.CompanyId,
            supportSession.IssuedByAdminUserId,
            supportSession.IssuedByAdminEmail,
            now,
            token,
            expiresAt);

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
            new SupportSessionRedeemedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                supportSession.IssuedByAdminUserId,
                now),
            cancellationToken);

        return Result.Success(response);
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
