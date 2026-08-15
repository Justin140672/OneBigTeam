using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.AdminCancelSubscription;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks). Calls the
/// same IStripeGateway as the customer-initiated CancelSubscription feature so Stripe's own state
/// stays authoritative — this is a support-initiated equivalent, not a purely local override.
/// </summary>
internal sealed class AdminCancelSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AdminCancelSubscriptionResponse>> HandleAsync(
        AdminCancelSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var previousState = new AdminCancelSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd);

        var cancelResult = subscription.AdminCancelAtPeriodEnd(now);
        if (cancelResult.IsFailure)
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(cancelResult.Error);
        }

        if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            await stripeGateway.CancelSubscriptionAsync(
                subscription.StripeSubscriptionId, atPeriodEnd: true, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new SubscriptionCancelledByAdminAuditEvent(
                subscription.CompanyId,
                currentUser.UserId,
                now,
                request.Reason,
                previousState,
                new AdminCancelSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd)),
            cancellationToken);

        return Result.Success(new AdminCancelSubscriptionResponse(subscription.CompanyId, subscription.CancelAtPeriodEnd));
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
