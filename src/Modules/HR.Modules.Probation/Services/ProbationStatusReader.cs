using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

internal sealed class ProbationStatusReader(ProbationDbContext dbContext) : IProbationStatusReader
{
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
