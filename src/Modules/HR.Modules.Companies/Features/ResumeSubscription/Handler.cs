using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.ResumeSubscription;

internal sealed class ResumeSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<ResumeSubscriptionResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<ResumeSubscriptionResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ResumeSubscriptionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            return Result.Failure<ResumeSubscriptionResponse>(
                Error.Validation("This company has no active Stripe subscription to resume."));
        }

        await stripeGateway.ResumeSubscriptionAsync(subscription.StripeSubscriptionId, cancellationToken);

        subscription.Resume(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new ResumeSubscriptionResponse(subscription.CancelAtPeriodEnd));
    }
}
