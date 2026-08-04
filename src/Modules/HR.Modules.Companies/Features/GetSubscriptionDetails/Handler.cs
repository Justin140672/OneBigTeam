using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.GetSubscriptionDetails;

internal sealed class GetSubscriptionDetailsHandler(
    CompaniesDbContext dbContext,
    IEmployeeDirectoryReader employeeDirectoryReader,
    ICurrentTenant currentTenant,
    IOptions<StripeOptions> stripeOptions)
{
    public async Task<Result<GetSubscriptionDetailsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<GetSubscriptionDetailsResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<GetSubscriptionDetailsResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        var employeeCount = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            companyId,
            new ReportFilterCriteria(EmployeeStatus: "Active"),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken);

        // Friendly name only when the subscription's stored price matches the configured plan
        // price id — otherwise fall back to the raw Stripe price id rather than inventing a
        // plan catalogue, per the plan's "don't over-engineer" guidance.
        var planName = subscription.PriceId is null
            ? null
            : subscription.PriceId == stripeOptions.Value.PriceId
                ? "Standard Plan"
                : subscription.PriceId;

        return Result.Success(new GetSubscriptionDetailsResponse(
            subscription.Status,
            planName,
            employeeCount.TotalCount,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd));
    }
}
