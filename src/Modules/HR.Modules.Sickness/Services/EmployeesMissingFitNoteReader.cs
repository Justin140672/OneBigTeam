using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// DSH-05 implementation of <see cref="IEmployeesMissingFitNoteReader"/>. Uses the same
/// Pending/Overdue evidence-request predicate as the Sickness module's own GetMissingFitNotes
/// feature so the manager team-status summary count and that feature's drill-down list agree.
/// </summary>
internal sealed class EmployeesMissingFitNoteReader(SicknessDbContext dbContext) : IEmployeesMissingFitNoteReader
{
    public async Task<IReadOnlySet<Guid>> GetEmployeeIdsMissingFitNotesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var missing = await (
            from evidenceRequest in dbContext.SicknessEvidenceRequests.AsNoTracking()
            join record in dbContext.SicknessRecords.AsNoTracking()
                on evidenceRequest.SicknessRecordId equals record.Id
            where evidenceRequest.CompanyId == companyId
               && (evidenceRequest.Status == SicknessEvidenceRequestStatus.Pending
                   || evidenceRequest.Status == SicknessEvidenceRequestStatus.Overdue)
               && ids.Contains(record.EmployeeId)
            select record.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return missing.ToHashSet();
    }
}
