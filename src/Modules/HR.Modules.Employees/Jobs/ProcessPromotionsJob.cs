using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

// Daily job that applies promotions whose effective date has arrived. Scans across all companies
// in one query (no per-tenant loop), mirroring ProcessLeavingEmployeesJob. The actual application
// (position/location/manager reassignment, completion, audit + integration events) is delegated to
// IEmployeePromotionFinalizer so PromoteEmployeeHandler can trigger the exact same idempotent path
// immediately when the effective date is today or backdated.
internal sealed class ProcessPromotionsJob(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IEmployeePromotionFinalizer promotionFinalizer,
    ILogger<ProcessPromotionsJob> logger)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var pendingPromotions = await dbContext.EmployeePromotions
            .Where(p => p.CompletedAt == null)
            .ToListAsync();

        if (pendingPromotions.Count == 0)
            return;

        var employeeIds = pendingPromotions.Select(p => p.EmployeeId).Distinct().ToList();

        var employeesById = await dbContext.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        // Companies may each have their own configured time zone, so "today" (used as the
        // effective-date due boundary) must be resolved per company rather than once globally.
        var todayByCompany = new Dictionary<Guid, DateOnly>();

        foreach (var promotion in pendingPromotions)
        {
            if (!employeesById.TryGetValue(promotion.EmployeeId, out var employee))
            {
                logger.LogWarning(
                    "Promotion {PromotionId} in company {CompanyId} references employee {EmployeeId} " +
                    "which was not found — skipping.",
                    promotion.Id,
                    promotion.CompanyId,
                    promotion.EmployeeId);
                continue;
            }

            if (!todayByCompany.TryGetValue(promotion.CompanyId, out var today))
            {
                var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(promotion.CompanyId, CancellationToken.None);
                today = clock.TodayIn(timeZoneId);
                todayByCompany[promotion.CompanyId] = today;
            }

            if (promotion.EffectiveDate <= today)
                await promotionFinalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, now, CancellationToken.None);
        }
    }
}
