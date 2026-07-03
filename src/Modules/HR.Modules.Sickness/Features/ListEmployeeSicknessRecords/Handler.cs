using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;

internal sealed class ListEmployeeSicknessRecordsHandler(SicknessDbContext db)
{
    public async Task<Result<ListEmployeeSicknessRecordsResponse>> HandleAsync(
        ListEmployeeSicknessRecordsRequest request,
        CancellationToken cancellationToken)
    {
        var records = await db.SicknessRecords
            .Where(r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId)
            .OrderByDescending(r => r.StartDate)
            .Select(r => new SicknessRecordSummary(
                r.Id,
                r.CompanyId,
                r.EmployeeId,
                r.CategoryId,
                r.Status,
                r.StartDate,
                r.StartDayPart,
                r.EndDate,
                r.TotalDays,
                r.EvidenceStatus,
                r.CreatedAt,
                r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListEmployeeSicknessRecordsResponse(records));
    }
}
