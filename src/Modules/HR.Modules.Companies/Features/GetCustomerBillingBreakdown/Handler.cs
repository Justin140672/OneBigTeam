using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Pricing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

internal sealed class GetCustomerBillingBreakdownHandler(
    CompaniesDbContext companiesDbContext,
    PlatformDbContext platformDbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IEmployeeDirectoryReader employeeDirectoryReader,
    IEmployeeStarterReader employeeStarterReader,
    IClock clock)
{
    private readonly CompaniesDbContext _dbContext = companiesDbContext;
    private readonly PlatformDbContext _platformDbContext = platformDbContext;

    public async Task<Result<GetCustomerBillingBreakdownResponse>> HandleAsync(
        GetCustomerBillingBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetCustomerBillingBreakdownResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var company = await _dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            return Result.Failure<GetCustomerBillingBreakdownResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);

        var activeEmployees = (await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(EmployeeStatus: "Active"),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken)).TotalCount;

        var leavers = (await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            company.Id,
            new ReportFilterCriteria(EmployeeStatus: "Leaving"),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken)).TotalCount;

        var futureStarters = (await employeeStarterReader.GetEmployeeStartersAsync(
            company.Id,
            new ReportFilterCriteria(DateRangeStart: today.AddDays(1)),
            new Pagination(PageNumber: 1, PageSize: 1),
            sortBy: null,
            sortDescending: false,
            cancellationToken)).TotalCount;

        var chargeableEmployees = activeEmployees + leavers;

        var platformSettings = await _platformDbContext.PlatformSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);
        var pricingConfig = platformSettings?.GetPricingConfig() ?? SubscriptionPricingConfig.Default;
        var breakdown = SubscriptionPricingCalculator.Calculate(chargeableEmployees, pricingConfig);

        var discounts = 0m;
        var monthlyTotal = breakdown.FinalMonthlyCharge - discounts;

        var pricePerEmployee = chargeableEmployees > 0
            ? breakdown.FinalMonthlyCharge / chargeableEmployees
            : 0m;

        var computedAt = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        var snapshot = CustomerBillingSnapshot.Create(
            company.Id,
            computedAt,
            activeEmployees,
            futureStarters,
            leavers,
            chargeableEmployees,
            pricePerEmployee,
            discounts,
            monthlyTotal);

        _dbContext.CustomerBillingSnapshots.Add(snapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var history = await _dbContext.CustomerBillingSnapshots
            .AsNoTracking()
            .Where(s => s.CompanyId == company.Id)
            .OrderByDescending(s => s.ComputedAt)
            .Take(20)
            .Select(s => new BillingSnapshotDto(
                s.Id,
                s.ComputedAt,
                s.ActiveEmployees,
                s.FutureStarters,
                s.Leavers,
                s.ChargeableEmployees,
                s.PricePerEmployee,
                s.Discounts,
                s.MonthlyTotal))
            .ToListAsync(cancellationToken);

        var response = new GetCustomerBillingBreakdownResponse(
            company.Id,
            computedAt,
            activeEmployees,
            futureStarters,
            leavers,
            chargeableEmployees,
            pricePerEmployee,
            discounts,
            monthlyTotal,
            history);

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
