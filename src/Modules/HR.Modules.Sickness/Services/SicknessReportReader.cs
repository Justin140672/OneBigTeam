using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class SicknessReportReader(SicknessDbContext dbContext) : ISicknessReportReader
{
    public async Task<IReadOnlyList<SicknessReportRecordItem>> GetSicknessRecordsAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId);

        // Overlap filtering: a record overlaps the requested range when its start is on/before the
        // range end (or there is no range end) and its effective end (EndDate, or "still open" which
        // is treated as never-ending) is on/after the range start (or there is no range start).
        if (startDate is not null)
            query = query.Where(r => r.EndDate == null || r.EndDate >= startDate);

        if (endDate is not null)
            query = query.Where(r => r.StartDate <= endDate);

        var records = await query
            .Select(r => new { r.EmployeeId, r.Id, r.StartDate, r.EndDate, r.TotalDays })
            .ToListAsync(cancellationToken);

        return records
            .Select(r => new SicknessReportRecordItem(
                r.EmployeeId,
                r.Id,
                r.StartDate,
                r.EndDate,
                r.TotalDays ?? 0m))
            .ToList();
    }
}
