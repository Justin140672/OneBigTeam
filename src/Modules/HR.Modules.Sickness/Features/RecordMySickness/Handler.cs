using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class RecordMySicknessHandler(SicknessDbContext db, IClock clock)
{
    public async Task<Result<RecordSicknessResponse>> HandleAsync(
        RecordMySicknessRequest request,
        CancellationToken cancellationToken)
    {
        var categoryExists = await db.SicknessCategories
            .AnyAsync(c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!categoryExists)
            return Result.Failure<RecordSicknessResponse>(Error.NotFound("Sickness category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = SicknessRecord.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.CategoryId,
            request.StartDate,
            request.StartDayPart,
            request.Notes,
            now);

        db.SicknessRecords.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecordSicknessResponse(
            entity.Id,
            entity.CompanyId,
            entity.EmployeeId,
            entity.CategoryId,
            entity.Status,
            entity.StartDate,
            entity.StartDayPart,
            entity.EvidenceStatus,
            entity.Notes,
            entity.CreatedAt,
            entity.UpdatedAt));
    }
}
