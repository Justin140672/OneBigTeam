using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

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

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new SupportSessionRevokedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                currentUser.UserId,
                now),
            cancellationToken);

        return Result.Success(new RevokeSupportSessionResponse(supportSession.Id, supportSession.RevokedAt!.Value));
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
