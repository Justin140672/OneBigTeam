using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class SicknessReportReader(SicknessDbContext dbContext) : ISicknessReportReader
{
    private const int MaxRows = 50_000;

    public async Task<IReadOnlyList<SicknessReportRecordItem>> GetSicknessRecordsAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId);

        if (startDate is not null)
            query = query.Where(r => r.EndDate == null || r.EndDate >= startDate);

        if (endDate is not null)
            query = query.Where(r => r.StartDate <= endDate);

        var records = await query
            .OrderBy(r => r.Id)
            .Take(MaxRows)
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
