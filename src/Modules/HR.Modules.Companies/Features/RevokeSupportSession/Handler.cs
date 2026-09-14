using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.RevokeSupportSession;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler/GetCustomerDetailsHandler
/// (see their remarks) — no first-class platform-administrator identity model exists yet.
/// </summary>
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
            return Result.Failure<RevokeSupportSessionResponse>(
                Error.NotFound($"No support session was found with id '{request.SupportSessionId}'."));
        }

        var now = clock.UtcNowOffset();
        var revokeResult = supportSession.Revoke(now);
        if (revokeResult.IsFailure)
        {
            return Result.Failure<RevokeSupportSessionResponse>(revokeResult.Error);
        }

        var response = new RevokeSupportSessionResponse(supportSession.Id, supportSession.RevokedAt!.Value);

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
            new SupportSessionRevokedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                currentUser.UserId,
                now),
            cancellationToken);

        return Result.Success(response);
    }

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
