using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeavePolicyProvisioner(LeaveDbContext dbContext, IClock clock) : ILeavePolicyProvisioner
{
    public async Task<Guid> EnsureDefaultLeavePolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var existingDefault = await dbContext.LeavePolicies
            .Where(policy => policy.CompanyId == companyId && policy.IsDefault && policy.IsActive)
            .Select(policy => (Guid?)policy.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDefault is not null)
        {
            return existingDefault.Value;
        }

        var now = clock.UtcNowOffset();

        var policy = LeavePolicy.Create(
            Guid.NewGuid(),
            companyId,
            "Standard",
            "Default leave policy",
            carryOverDays: 5,
            allowNegativeBalance: false,
            isDefault: true,
            now);

        dbContext.LeavePolicies.Add(policy);
        await dbContext.SaveChangesAsync(cancellationToken);

        return policy.Id;
    }
}
