using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.LiftCompanyLegalHold;

/// <summary>
/// NFR-07: same defense-in-depth allow-list gate as ScheduleCustomerDeletionHandler. Lifts an
/// active company-wide legal hold.
/// </summary>
internal sealed class LiftCompanyLegalHoldHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<LiftCompanyLegalHoldResponse>> HandleAsync(
        LiftCompanyLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<LiftCompanyLegalHoldResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<LiftCompanyLegalHoldResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();

        var result = subscription.LiftLegalHold(now);
        if (result.IsFailure)
        {
            return Result.Failure<LiftCompanyLegalHoldResponse>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new CompanyLegalHoldLiftedAuditEvent(
                subscription.CompanyId, currentUser.UserId, now, request.Reason.Trim()),
            cancellationToken);

        return Result.Success(new LiftCompanyLegalHoldResponse(subscription.CompanyId, now));
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
