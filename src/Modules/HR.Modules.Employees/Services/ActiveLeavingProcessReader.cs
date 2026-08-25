using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

// OFF-03: implementation of the port Offboarding's reconciliation job uses to find leaving
// processes with no corresponding active offboarding plan.
internal sealed class ActiveLeavingProcessReader(EmployeesDbContext dbContext) : IActiveLeavingProcessReader
{
    public async Task<IReadOnlyList<ActiveLeavingProcessItem>> GetInProgressLeavingProcessesAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(p => p.Status == LeavingProcessStatus.InProgress)
            .Select(p => new ActiveLeavingProcessItem(p.CompanyId, p.EmployeeId, p.LastWorkingDay))
            .ToListAsync(cancellationToken);
    }
}
