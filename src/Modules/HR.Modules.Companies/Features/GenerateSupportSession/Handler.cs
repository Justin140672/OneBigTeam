using System.Security.Cryptography;
using System.Text;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GenerateSupportSession;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler/GetCustomerDetailsHandler
/// (see their remarks) — no first-class platform-administrator identity model exists yet.
/// </summary>
internal sealed class GenerateSupportSessionHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<GenerateSupportSessionResponse>> HandleAsync(
        GenerateSupportSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GenerateSupportSessionResponse>(
                Error.Unauthorized("This account is not authorised to generate customer support sessions."));
        }

        var companyExists = await dbContext.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (!companyExists)
        {
            return Result.Failure<GenerateSupportSessionResponse>(
                Error.NotFound($"No company was found with id '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        var supportSession = SupportSession.Issue(
            request.CompanyId,
            currentUser.UserId ?? Guid.Empty,
            currentUser.Email ?? string.Empty,
            request.Reason,
            tokenHash,
            now);

        dbContext.SupportSessions.Add(supportSession);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new SupportSessionGeneratedAuditEvent(
                supportSession.CompanyId,
                supportSession.Id,
                currentUser.UserId,
                now,
                request.Reason),
            cancellationToken);

        return Result.Success(new GenerateSupportSessionResponse(
            supportSession.Id, supportSession.CompanyId, supportSession.ExpiresAt, token));
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
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
