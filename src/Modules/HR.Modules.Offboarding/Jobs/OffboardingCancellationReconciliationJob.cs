using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Offboarding.Jobs;

internal sealed class OffboardingCancellationReconciliationJob(
    OffboardingDbContext dbContext,
    IOffboardingPlanCoordinator offboardingPlanCoordinator,
    ILogger<OffboardingCancellationReconciliationJob> logger)
{
    public async Task ExecuteAsync()
    {
        var cancelledPlans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.Status == OffboardingStatus.Cancelled)
            .Select(p => new { p.CompanyId, p.EmployeeId })
            .Distinct()
            .ToListAsync();

        if (cancelledPlans.Count == 0)
            return;

        foreach (var plan in cancelledPlans)
        {
            try
            {
                await offboardingPlanCoordinator.CancelOutstandingTasksAsync(
                    plan.CompanyId, plan.EmployeeId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Offboarding cancellation reconciliation failed for employee {EmployeeId} in company {CompanyId}.",
                    plan.EmployeeId, plan.CompanyId);
            }
        }
    }
}
