using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.EndSupportSession;

internal sealed class EndSupportSessionHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<EndSupportSessionResponse>> HandleAsync(
        EndSupportSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsSupportSession || currentUser.SupportSessionId is not { } supportSessionId)
        {
            return Result.Failure<EndSupportSessionResponse>(
                Error.Unauthorized("Only an active support session can be ended through this endpoint."));
        }

        var supportSession = await dbContext.SupportSessions
            .SingleOrDefaultAsync(s => s.Id == supportSessionId, cancellationToken);

        var now = clock.UtcNowOffset();

        if (supportSession is null)
        {
            await PublishAsync(Guid.Empty, supportSessionId, "not_found", now);
            return Result.Failure<EndSupportSessionResponse>(Error.NotFound("The support session was not found."));
        }

        if (!Guid.TryParse(currentUser.TenantId, out var tokenCompanyId) || tokenCompanyId != supportSession.CompanyId)
        {
            await PublishAsync(supportSession.CompanyId, supportSessionId, "company_mismatch", now);
            return Result.Failure<EndSupportSessionResponse>(
                Error.Unauthorized("The support session does not match the current company."));
        }

        var expectedVersion = supportSession.Version;
        var revokeResult = supportSession.Revoke(now);
        if (revokeResult.IsFailure)
        {
            await PublishAsync(supportSession.CompanyId, supportSessionId, "already_revoked", now);
            return Result.Failure<EndSupportSessionResponse>(revokeResult.Error);
        }

        var saveResult = await dbContext.SaveChangesWithConcurrencyAsync(
            supportSession, expectedVersion, "This support session was changed by another request.", cancellationToken);
        if (saveResult.IsFailure)
        {
            await PublishAsync(supportSession.CompanyId, supportSessionId, "concurrency_conflict", now);
            return Result.Failure<EndSupportSessionResponse>(saveResult.Error);
        }

        await PublishAsync(supportSession.CompanyId, supportSessionId, "revoked", now);

        return Result.Success(new EndSupportSessionResponse(supportSession.Id, supportSession.RevokedAt!.Value));
    }

    private Task PublishAsync(Guid companyId, Guid supportSessionId, string outcome, DateTimeOffset now) =>
        auditEventPublisher.PublishAsync(
            new SupportSessionEndedAuditEvent(companyId, supportSessionId, currentUser.UserId, outcome, now),
            CancellationToken.None);
}
