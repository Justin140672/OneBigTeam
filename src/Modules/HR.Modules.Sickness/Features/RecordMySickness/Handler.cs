using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class RecordMySicknessHandler(
    SicknessDbContext db,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IPublicHolidayReader publicHolidayReader)
{
    public async Task<Result<RecordSicknessResponse>> HandleAsync(
        RecordMySicknessRequest request,
        CancellationToken cancellationToken)
    {
        var categoryExists = await db.SicknessCategories
            .AnyAsync(c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!categoryExists)
            return Result.Failure<RecordSicknessResponse>(Error.NotFound("Sickness category not found."));

        decimal? totalDays = null;

        if (request.EndDate.HasValue)
        {
            var endDayPart = request.EndDayPart ?? SicknessDayPart.FullDay;
            var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
                request.CompanyId, request.EmployeeId, cancellationToken);

            var sicknessSettings = await sicknessSettingsReader.GetSicknessSettingsAsync(
                request.CompanyId, cancellationToken);

            IReadOnlyCollection<DateOnly>? publicHolidays = null;
            if (sicknessSettings.ExcludePublicHolidaysFromSickness)
            {
                var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                    request.CompanyId, request.StartDate, request.EndDate.Value, cancellationToken);
                publicHolidays = holidays.Select(h => h.Date).ToList();
            }

            totalDays = SicknessCalculator.CalculateTotalDays(
                request.StartDate, request.StartDayPart,
                request.EndDate.Value, endDayPart,
                workingPattern, publicHolidays);
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = SicknessRecord.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.CategoryId,
            request.StartDate,
            request.StartDayPart,
            request.EndDate,
            request.EndDayPart,
            totalDays,
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
