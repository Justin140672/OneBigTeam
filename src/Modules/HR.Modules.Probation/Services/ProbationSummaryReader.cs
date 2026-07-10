using HR.Modules.Probation.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

internal sealed class ProbationSummaryReader(ProbationDbContext dbContext) : IProbationSummaryReader
{
    public async Task<ProbationSummaryItem?> GetSummaryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.EmployeeId == employeeId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
            return null;

        return new ProbationSummaryItem(
            record.Status.ToString(),
            record.StartDate,
            record.ExpectedEndDate,
            record.DecisionDate);
    }
}
