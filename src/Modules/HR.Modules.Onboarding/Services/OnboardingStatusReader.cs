using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Services;

internal sealed class OnboardingStatusReader(OnboardingDbContext dbContext) : IOnboardingStatusReader
{
    public async Task<OnboardingStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var status = await dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (OnboardingStatus?)p.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return status is null ? null : new OnboardingStatusSummary(status.Value.ToString());
    }
}
