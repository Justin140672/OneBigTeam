using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetMySicknessRecords;

internal sealed class GetMySicknessRecordsHandler(SicknessDbContext db)
{
    public async Task<Result<GetMySicknessRecordsResponse>> HandleAsync(
        GetMySicknessRecordsRequest request,
        CancellationToken cancellationToken)
    {
        var records = await db.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId)
            .OrderByDescending(r => r.StartDate)
            .Select(r => new MySicknessRecordSummary(
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

        return Result.Success(new GetMySicknessRecordsResponse(records));
    }
}
