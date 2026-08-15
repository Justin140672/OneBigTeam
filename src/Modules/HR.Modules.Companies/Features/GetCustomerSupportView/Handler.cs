using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetCustomerSupportView;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDetailsHandler/GetCustomerBillingBreakdownHandler
/// (see their remarks) — no first-class platform-administrator identity model exists yet, so the
/// caller's email must additionally appear in the "PlatformAdmin:AllowedEmails" configuration
/// allow-list.
/// </summary>
internal sealed class GetCustomerSupportViewHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IEmployeeDirectoryReader employeeDirectoryReader,
    ICompanyUserCountReader companyUserCountReader,
    IBackgroundJobStatusReader backgroundJobStatusReader)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<GetCustomerSupportViewResponse>> HandleAsync(
        GetCustomerSupportViewRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetCustomerSupportViewResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var company = await _dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            return Result.Failure<GetCustomerSupportViewResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
        }

        var subscription = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        // Sequential, not Task.WhenAll — see GetCustomerDetailsHandler's matching fix/remarks.
        // activeEmployeeCount/totalEmployeeCount both go through the same scoped
        // IEmployeeDirectoryReader (one shared EmployeesDbContext), and running them concurrently
        // throws "A second operation was started on this context instance before a previous
        // operation completed." Every call here is made sequential rather than reasoning about
        // exactly which subset shares a DbContext with which — the safest way to rule this whole
        // class of bug out for this handler.
        var activeEmployeeCount = (await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(EmployeeStatus: "Active"),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken)).TotalCount;

        var totalEmployeeCount = (await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken)).TotalCount;

        var userCount = await companyUserCountReader.GetUserCountAsync(company.Id, cancellationToken);

        var recentBillingSnapshots = await _dbContext.CustomerBillingSnapshots
            .AsNoTracking()
            .Where(s => s.CompanyId == company.Id)
            .OrderByDescending(s => s.ComputedAt)
            .Take(5)
            .Select(s => new SupportBillingSnapshotDto(s.ComputedAt, s.ChargeableEmployees, s.MonthlyTotal))
            .ToListAsync(cancellationToken);

        var jobStatus = backgroundJobStatusReader.GetStatus();

        var response = new GetCustomerSupportViewResponse(
            company.Id,
            company.Name,
            company.Status.ToString(),
            subscription?.Status.ToString() ?? "None",
            subscription?.TrialStartedAt,
            subscription?.TrialExpiresAt,
            subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false,
            subscription?.AdminForcedReadOnly ?? false,
            userCount,
            activeEmployeeCount,
            totalEmployeeCount,
            recentBillingSnapshots,
            jobStatus.Available,
            jobStatus.ServerCount,
            jobStatus.Enqueued,
            jobStatus.Processing,
            jobStatus.Scheduled,
            jobStatus.Failed,
            jobStatus.Succeeded,
            jobStatus.Recurring,
            RecentErrorsAvailable: false,
            RecentEmailsAvailable: false,
            RecentLoginActivityAvailable: false);

        return Result.Success(response);
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
