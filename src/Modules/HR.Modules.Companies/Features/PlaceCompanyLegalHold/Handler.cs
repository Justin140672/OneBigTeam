using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.PlaceCompanyLegalHold;

/// <summary>
/// NFR-07: same defense-in-depth allow-list gate as ScheduleCustomerDeletionHandler. Places a
/// company-wide legal hold so all retention deletion (automated jobs and operator purge endpoints)
/// skips this company until the hold is lifted.
/// </summary>
internal sealed class PlaceCompanyLegalHoldHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<PlaceCompanyLegalHoldResponse>> HandleAsync(
        PlaceCompanyLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<PlaceCompanyLegalHoldResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<PlaceCompanyLegalHoldResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();

        var result = subscription.PlaceLegalHold(currentUser.UserId, request.Reason, now);
        if (result.IsFailure)
        {
            return Result.Failure<PlaceCompanyLegalHoldResponse>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new CompanyLegalHoldPlacedAuditEvent(
                subscription.CompanyId, currentUser.UserId, now, request.Reason.Trim()),
            cancellationToken);

        return Result.Success(new PlaceCompanyLegalHoldResponse(
            subscription.CompanyId, subscription.LegalHoldPlacedAt!.Value));
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
