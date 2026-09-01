using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Pricing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDetailsHandler/GetCustomerDashboardHandler/
/// ListCustomersHandler (see their remarks) — no first-class platform-administrator identity model
/// exists yet, so the caller's email must additionally appear in the "PlatformAdmin:AllowedEmails"
/// configuration allow-list.
/// </summary>
internal sealed class GetCustomerBillingBreakdownHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IEmployeeDirectoryReader employeeDirectoryReader,
    IEmployeeStarterReader employeeStarterReader,
    IOptions<StripeOptions> stripeOptions,
    IClock clock)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

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

        // Sequential, not Task.WhenAll — employeeDirectoryReader and employeeStarterReader are
        // both backed by the same scoped (request-shared) EmployeesDbContext, and EF Core's
        // DbContext is not safe for concurrent operations on the same instance. Running these
        // "in parallel" throws "A second operation was started on this context instance before
        // a previous operation completed" — see GetCustomerDetailsHandler's matching fix/remarks
        // for the full explanation, including why this surfaced as a misleading "not authorised"
        // banner on the frontend rather than an obvious crash.
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

        // Employees currently on the books being billed this period; future starters are not yet
        // chargeable since they haven't started, and former employees have already left and are
        // excluded from both the Active and Leaving statuses.
        var chargeableEmployees = activeEmployees + leavers;

        // Story 4 — the monthly charge now comes from the single authoritative configurable
        // progressive pricing model (PlatformSettings singleton), not a flat per-employee rate.
        // Falls back to the built-in default when the singleton has never been seeded (e.g. tests).
        var platformSettings = await _dbContext.PlatformSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);
        var pricingConfig = platformSettings?.GetPricingConfig() ?? SubscriptionPricingConfig.Default;
        var breakdown = SubscriptionPricingCalculator.Calculate(chargeableEmployees, pricingConfig);

        // No discount/promotional-pricing concept exists anywhere in the codebase yet (verified —
        // StripeOptions, CustomerSubscription and Company all lack one), so this is hardcoded to
        // zero rather than inventing a discount system. The API/UI must present this honestly
        // rather than imply real discount data exists.
        var discounts = 0m;
        var monthlyTotal = breakdown.FinalMonthlyCharge - discounts;

        // The snapshot/response keeps a single pricePerEmployee field for backwards compatibility;
        // under progressive pricing it is the effective (blended) rate for this employee count.
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
