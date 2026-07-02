using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetSicknessRecord;

internal sealed class GetSicknessRecordHandler(SicknessDbContext db)
{
    public async Task<Result<GetSicknessRecordResponse>> HandleAsync(
        GetSicknessRecordRequest request,
        CancellationToken cancellationToken)
    {
        var record = await db.SicknessRecords
            .FirstOrDefaultAsync(r =>
                r.Id == request.Id &&
                r.CompanyId == request.CompanyId &&
                r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure<GetSicknessRecordResponse>(Error.NotFound("Sickness record not found."));

        return Result.Success(new GetSicknessRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.CategoryId,
            record.Status,
            record.StartDate,
            record.StartDayPart,
            record.EndDate,
            record.EndDayPart,
            record.ReturnToWorkDate,
            record.EvidenceStatus,
            record.EvidenceNotes,
            record.Notes,
            record.TotalDays,
            record.CreatedAt,
            record.UpdatedAt));
    }
}
