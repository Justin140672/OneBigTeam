using System.Security.Cryptography;
using System.Text;

using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed class RedeemSupportSessionHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
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

        var supportSession = await dbContext.SupportSessions
            .SingleOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);

        if (supportSession is null)
        {
            return Result.Failure<RedeemSupportSessionResponse>(
                Error.NotFound("No matching support session was found for this token."));
        }

        var now = clock.UtcNowOffset();
        var redeemResult = supportSession.Redeem(now);
        if (redeemResult.IsFailure)
        {
            return Result.Failure<RedeemSupportSessionResponse>(redeemResult.Error);
        }

        var response = new RedeemSupportSessionResponse(
            supportSession.CompanyId,
            supportSession.IssuedByAdminUserId,
            supportSession.IssuedByAdminEmail,
            supportSession.RedeemedAt!.Value);

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
