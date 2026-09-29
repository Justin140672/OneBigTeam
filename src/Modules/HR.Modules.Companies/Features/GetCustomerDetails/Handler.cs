using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.GetCustomerDetails;

internal sealed class GetCustomerDetailsHandler(
    CompaniesDbContext dbContext,
    HR.SharedKernel.ICurrentUser currentUser,
    IConfiguration configuration,
    IEmployeeDirectoryReader employeeDirectoryReader,
    IOptions<StripeOptions> stripeOptions,
    IDocumentStorageReader documentStorageReader)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<GetCustomerDetailsResponse>> HandleAsync(
        GetCustomerDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetCustomerDetailsResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var company = await _dbContext.Companies
            .Include(c => c.Settings)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            return Result.Failure<GetCustomerDetailsResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
        }

        var subscription = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        var activeEmployeeCount = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(EmployeeStatus: "Active"),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken);

        var totalEmployeeCount = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken);

        var storageUsage = await documentStorageReader.GetStorageUsageAsync(company.Id, cancellationToken);

        var monthlyCharge = subscription?.Status == SubscriptionStatus.Active
            ? stripeOptions.Value.MonthlyPriceGbp
            : (decimal?)null;

        var settings = company.Settings;
        var settingsDto = settings is null
            ? null
            : new CustomerDetailsSettingsDto(
                settings.TimeZone,
                settings.Locale,
                settings.WorkingDays,
                settings.HoursPerDay,
                settings.LeaveYearStartMonth,
                settings.DefaultHolidayAllowance,
                settings.ProbationMonths,
                settings.EmployeeNumberMode,
                settings.EmployeeNumberPrefix,
                settings.NextEmployeeNumber);

        var response = new GetCustomerDetailsResponse(
            company.Id,
            company.Name,
            company.Status.ToString(),
            company.CreatedAt,
            company.UpdatedAt,
            subscription?.Status.ToString() ?? "None",
            subscription?.TrialStartedAt,
            subscription?.TrialExpiresAt,
            subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false,
            subscription?.AdminForcedReadOnly ?? false,
            monthlyCharge,
            activeEmployeeCount.TotalCount,
            totalEmployeeCount.TotalCount,
            storageUsage.TotalStorageBytes,
            storageUsage.FileCount,
            settingsDto);

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
