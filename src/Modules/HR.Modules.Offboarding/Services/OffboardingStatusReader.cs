using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingStatusReader(OffboardingDbContext dbContext) : IOffboardingStatusReader
{
    public async Task<OffboardingStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var status = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (OffboardingStatus?)p.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return status is null ? null : new OffboardingStatusSummary(status.Value.ToString());
    }
}
