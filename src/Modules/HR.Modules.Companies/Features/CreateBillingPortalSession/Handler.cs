using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.CreateBillingPortalSession;

internal sealed class CreateBillingPortalSessionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    IConfiguration configuration)
{
    // Mirrors CreateCheckoutSessionHandler's base-URL resolution pattern.
    private const string FallbackWebAppBaseUrl = "http://localhost:5270";

    public async Task<Result<CreateBillingPortalSessionResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<CreateBillingPortalSessionResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<CreateBillingPortalSessionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeCustomerId))
        {
            return Result.Failure<CreateBillingPortalSessionResponse>(
                Error.Validation("This company has no Stripe customer to manage billing for."));
        }

        var webAppBaseUrl = (
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            FallbackWebAppBaseUrl).TrimEnd('/');

        var returnUrl = $"{webAppBaseUrl}/subscription";

        var portalUrl = await stripeGateway.CreateBillingPortalSessionAsync(
            subscription.StripeCustomerId, returnUrl, cancellationToken);

        return Result.Success(new CreateBillingPortalSessionResponse(portalUrl));
    }
}
