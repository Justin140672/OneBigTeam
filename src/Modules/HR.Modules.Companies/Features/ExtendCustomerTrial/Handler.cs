using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDetailsHandler/GetCustomerDashboardHandler
/// (see their remarks) — no first-class platform-administrator identity model exists yet.
/// </summary>
internal sealed class ExtendCustomerTrialHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ExtendCustomerTrialResponse>> HandleAsync(
        ExtendCustomerTrialRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ExtendCustomerTrialResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ExtendCustomerTrialResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var previousState = new TrialExtendedAuditSnapshot(subscription.Status.ToString(), subscription.TrialExpiresAt);

        var extendResult = subscription.ExtendTrial(request.NewTrialExpiresAt, now);
        if (extendResult.IsFailure)
        {
            return Result.Failure<ExtendCustomerTrialResponse>(extendResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new TrialExtendedAuditEvent(
                subscription.CompanyId,
                currentUser.UserId,
                now,
                request.Reason,
                previousState,
                new TrialExtendedAuditSnapshot(subscription.Status.ToString(), subscription.TrialExpiresAt)),
            cancellationToken);

        return Result.Success(new ExtendCustomerTrialResponse(
            subscription.CompanyId, subscription.Status.ToString(), subscription.TrialExpiresAt));
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
