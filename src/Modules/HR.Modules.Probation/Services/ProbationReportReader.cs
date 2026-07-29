using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

/// <summary>
/// Company-wide (optionally employee-id-filtered) probation reader backing the Probation Report
/// (OBT-711). Distinct from ProbationSummaryReader, which only reads a single employee's latest
/// record and cannot serve a company-wide/manager-scoped report.
/// </summary>
internal sealed class ProbationReportReader(ProbationDbContext dbContext) : IProbationReportReader
{
    public async Task<IReadOnlyList<ProbationReportItem>> GetProbationReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId);

        if (employeeIds is not null)
            query = query.Where(r => employeeIds.Contains(r.EmployeeId));

        var records = await query
            .Select(r => new { r.Id, r.EmployeeId, r.Status, r.StartDate, r.ExpectedEndDate })
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
            return [];

        var recordIds = records.Select(r => r.Id).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var pendingReviews = await dbContext.ProbationReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                && recordIds.Contains(r.ProbationRecordId)
                && r.Status == ProbationReviewStatus.Pending)
            .Select(r => new { r.ProbationRecordId, r.DueDate })
            .ToListAsync(cancellationToken);

        var overdueByRecord = pendingReviews
            .Where(r => r.DueDate < today)
            .GroupBy(r => r.ProbationRecordId)
            .ToDictionary(g => g.Key, g => g.Count());

        var dueByRecord = pendingReviews
            .Where(r => r.DueDate >= today)
            .GroupBy(r => r.ProbationRecordId)
            .ToDictionary(g => g.Key, g => g.Count());

        return records
            .Select(r => new ProbationReportItem(
                r.EmployeeId,
                r.Id,
                r.Status.ToString(),
                r.StartDate,
                r.ExpectedEndDate,
                dueByRecord.GetValueOrDefault(r.Id, 0),
                overdueByRecord.GetValueOrDefault(r.Id, 0)))
            .ToList();
    }
}
