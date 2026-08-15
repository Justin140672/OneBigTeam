using System.Security.Cryptography;
using System.Text;

using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

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

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new SupportSessionRedeemedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                supportSession.IssuedByAdminUserId,
                now),
            cancellationToken);

        return Result.Success(new RedeemSupportSessionResponse(
            supportSession.CompanyId,
            supportSession.IssuedByAdminUserId,
            supportSession.IssuedByAdminEmail,
            supportSession.RedeemedAt!.Value));
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
