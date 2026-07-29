using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingDetailReader(OffboardingDbContext dbContext) : IOffboardingDetailReader
{
    public async Task<OffboardingDetail?> GetDetailAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Status, p.LastWorkingDay })
            .FirstOrDefaultAsync(cancellationToken);

        return plan is null ? null : new OffboardingDetail(plan.Status.ToString(), plan.LastWorkingDay);
    }
}
