using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

internal sealed class ProbationStatusReader(ProbationDbContext dbContext) : IProbationStatusReader
{
    // Same ordering as GetProbationRecordByEmployeeHandler (StartDate first, CreatedAt as
    // tiebreaker) so "should the tab show" and "which record the tab would display" can never
    // disagree.
    public async Task<ProbationStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var status = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => (ProbationStatus?)r.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return status is null ? null : new ProbationStatusSummary(status.Value.ToString());
    }
}
