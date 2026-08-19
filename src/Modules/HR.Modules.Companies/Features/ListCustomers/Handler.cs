using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.ListCustomers;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDashboardHandler (see its remarks) — no
/// first-class platform-administrator identity model exists yet, so the caller's email must
/// additionally appear in the "PlatformAdmin:AllowedEmails" configuration allow-list.
/// </summary>
internal sealed class ListCustomersHandler(
    CompaniesDbContext dbContext,
    HR.SharedKernel.ICurrentUser currentUser,
    IConfiguration configuration,
    IEmployeeDirectoryReader employeeDirectoryReader,
    ICompanyUserEmailSearchReader companyUserEmailSearchReader,
    IOptions<StripeOptions> stripeOptions)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<ListCustomersResponse>> HandleAsync(
        ListCustomersRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ListCustomersResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var companies = await _dbContext.Companies
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var subscriptions = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var subscriptionsByCompanyId = subscriptions
            .ToDictionary(s => s.CompanyId, s => s);

        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (Guid.TryParse(search, out var searchGuid))
            {
                companies = companies
                    .Where(c => c.Id == searchGuid)
                    .ToList();
            }
            else
            {
                var matchedByEmail = await companyUserEmailSearchReader.FindCompanyIdsByEmailAsync(
                    search,
                    cancellationToken);

                companies = companies
                    .Where(c =>
                        c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || matchedByEmail.Contains(c.Id))
                    .ToList();
            }
        }

        // Sequential, not one concurrent GetEmployeeDirectoryAsync call per company via
        // Task.WhenAll — see GetCustomerDetailsHandler's matching fix/remarks. Every company here
        // shares the same scoped IEmployeeDirectoryReader (one EmployeesDbContext for the whole
        // request), so running more than one of these calls concurrently throws "A second
        // operation was started on this context instance before a previous operation completed"
        // as soon as there are 2+ companies — i.e. on every real call to this endpoint.
        var employeeCountByCompanyId = new Dictionary<Guid, int>();
        foreach (var company in companies)
        {
            var count = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
                company.Id,
                new ReportFilterCriteria(EmployeeStatus: "Active"),
                new Pagination(PageNumber: 1, PageSize: 1),
                sortBy: null,
                sortDescending: false,
                cancellationToken);

            employeeCountByCompanyId[company.Id] = count.TotalCount;
        }

        var items = companies
            .Select(company =>
            {
                subscriptionsByCompanyId.TryGetValue(company.Id, out var subscription);

                var monthlyCharge = subscription?.Status == SubscriptionStatus.Active
                    ? stripeOptions.Value.MonthlyPriceGbp
                    : (decimal?)null;

                return new CustomerListItemDto(
                    company.Id,
                    company.Name,
                    subscription?.Status.ToString() ?? "None",
                    employeeCountByCompanyId[company.Id],
                    monthlyCharge,
                    subscription?.TrialExpiresAt,
                    company.CreatedAt);
            })
            .ToList();

        return Result.Success(new ListCustomersResponse(items));
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
