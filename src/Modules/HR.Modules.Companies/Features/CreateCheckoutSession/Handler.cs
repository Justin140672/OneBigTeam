using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.CreateCheckoutSession;

internal sealed class CreateCheckoutSessionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IConfiguration configuration)
{
    // Mirrors the base-URL resolution pattern used by HR.Marketing's SiteHeader.razor for
    // linking into HR.Web from another Aspire-hosted project.
    private const string FallbackWebAppBaseUrl = "http://localhost:5157";

    public async Task<Result<CreateCheckoutSessionResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<CreateCheckoutSessionResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        if (string.IsNullOrWhiteSpace(currentUser.Email))
        {
            return Result.Failure<CreateCheckoutSessionResponse>(
                Error.Unauthorized("No email address could be resolved for the current user."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<CreateCheckoutSessionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        var webAppBaseUrl = (
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            FallbackWebAppBaseUrl).TrimEnd('/');

        var successUrl = $"{webAppBaseUrl}/subscription?checkout=success";
        var cancelUrl = $"{webAppBaseUrl}/subscription?checkout=cancelled";

        var checkoutUrl = await stripeGateway.CreateCheckoutSessionAsync(
            companyId,
            currentUser.Email,
            subscription.StripeCustomerId,
            successUrl,
            cancelUrl,
            cancellationToken);

        return Result.Success(new CreateCheckoutSessionResponse(checkoutUrl));
    }
}
