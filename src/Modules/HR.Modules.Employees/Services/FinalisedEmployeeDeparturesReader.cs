using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class FinalisedEmployeeDeparturesReader(EmployeesDbContext dbContext) : IFinalisedEmployeeDeparturesReader
{
    public async Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        return await dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(p => p.FinalisationCompletedAt != null && p.FinalisationCompletedAt >= since)
            .Select(p => new FinalisedEmployeeDeparture(
                p.CompanyId, p.EmployeeId, p.Id, p.LeavingDate, p.FinalisationCompletedAt!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesPageAsync(
        DateTimeOffset? afterFinalisationCompletedAt,
        Guid? afterEmployeeId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(p => p.FinalisationCompletedAt != null);

        if (afterFinalisationCompletedAt is not null && afterEmployeeId is not null)
        {
            var afterCompleted = afterFinalisationCompletedAt.Value;
            var afterEmployee = afterEmployeeId.Value;

            query = query.Where(p =>
                p.FinalisationCompletedAt > afterCompleted
                || (p.FinalisationCompletedAt == afterCompleted && p.EmployeeId > afterEmployee));
        }

        return await query
            .OrderBy(p => p.FinalisationCompletedAt)
            .ThenBy(p => p.EmployeeId)
            .Take(take)
            .Select(p => new FinalisedEmployeeDeparture(
                p.CompanyId, p.EmployeeId, p.Id, p.LeavingDate, p.FinalisationCompletedAt!.Value))
            .ToListAsync(cancellationToken);
    }
}
