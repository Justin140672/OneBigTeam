using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

/// <summary>
/// ADM-02 Compliance Centre reader for company-wide pending probation reviews. One row per pending
/// review (not per record) so the Compliance Centre can list each review individually and classify
/// it overdue / due-soon from its own due date.
/// </summary>
internal sealed class ProbationReviewComplianceReader(ProbationDbContext dbContext)
    : IProbationReviewComplianceReader
{
    private const int MaxRows = 50_000;

    public async Task<IReadOnlyList<ProbationReviewComplianceItem>> GetPendingProbationReviewsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.Id, r.EmployeeId })
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
            return [];

        var recordMap = records.ToDictionary(r => r.Id, r => r.EmployeeId);
        var recordIds = recordMap.Keys.ToList();

        var reviews = await dbContext.ProbationReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && recordIds.Contains(r.ProbationRecordId)
                     && r.Status == ProbationReviewStatus.Pending)
            .OrderBy(r => r.DueDate)
            .Take(MaxRows)
            .Select(r => new { r.Id, r.ProbationRecordId, r.ReviewType, r.DueDate })
            .ToListAsync(cancellationToken);

        return reviews
            .Select(r => new ProbationReviewComplianceItem(
                recordMap[r.ProbationRecordId], r.Id, r.ReviewType.ToString(), r.DueDate))
            .ToList();
    }
}
