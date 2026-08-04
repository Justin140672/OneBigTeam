using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.CancelSubscription;

internal sealed class CancelSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<CancelSubscriptionResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.Validation("This company has no active Stripe subscription to cancel."));
        }

        await stripeGateway.CancelSubscriptionAsync(
            subscription.StripeSubscriptionId, atPeriodEnd: true, cancellationToken);

        subscription.RequestCancellation(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CancelSubscriptionResponse(subscription.CancelAtPeriodEnd));
    }
}
