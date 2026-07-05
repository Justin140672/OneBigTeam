using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeavePolicyReader(LeaveDbContext dbContext) : ILeavePolicyReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid leavePolicyId, CancellationToken cancellationToken)
        => dbContext.LeavePolicies.AnyAsync(
            lp => lp.Id == leavePolicyId && lp.CompanyId == companyId && lp.IsActive,
            cancellationToken);
}
