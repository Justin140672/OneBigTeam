using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

// Gap-2 reliability fix: implementation of the port consuming modules (e.g. Leave) use to
// reconcile "a departure Employees considers fully finalised has no corresponding required record
// of my own" — see IFinalisedEmployeeDeparturesReader for why this class of gap exists and why a
// consumer's own existing pending/failed-record retry sweep can never catch it. Mirrors
// ActiveLeavingProcessReader's cross-company scan shape (OFF-03).
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
}
