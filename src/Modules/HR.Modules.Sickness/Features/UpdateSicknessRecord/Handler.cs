using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed class UpdateSicknessRecordHandler(
    SicknessDbContext db,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IPublicHolidayReader publicHolidayReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<UpdateSicknessRecordResponse>> HandleAsync(
        UpdateSicknessRecordRequest request,
        CancellationToken cancellationToken)
    {
        var record = await db.SicknessRecords
            .FirstOrDefaultAsync(r =>
                r.Id == request.Id &&
                r.CompanyId == request.CompanyId &&
                r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure<UpdateSicknessRecordResponse>(Error.NotFound("Sickness record not found."));

        var categoryExists = await db.SicknessCategories
            .AnyAsync(c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!categoryExists)
            return Result.Failure<UpdateSicknessRecordResponse>(Error.Conflict("Sickness category not found."));

        decimal? totalDays = record.TotalDays;

        if (record.EndDate.HasValue)
        {
            var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
                request.CompanyId, request.EmployeeId, cancellationToken);

            var sicknessSettings = await sicknessSettingsReader.GetSicknessSettingsAsync(
                request.CompanyId, cancellationToken);

            IReadOnlyCollection<DateOnly>? publicHolidays = null;
            if (sicknessSettings.ExcludePublicHolidaysFromSickness)
            {
                var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                    request.CompanyId, request.StartDate, record.EndDate.Value, cancellationToken);
                publicHolidays = holidays.Select(h => h.Date).ToList();
            }

            totalDays = SicknessCalculator.CalculateTotalDays(
                request.StartDate, request.StartDayPart,
                record.EndDate.Value, record.EndDayPart ?? SicknessDayPart.FullDay,
                workingPattern, publicHolidays);
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        record.Update(
            request.CategoryId,
            request.StartDate,
            request.StartDayPart,
            record.EndDate,
            record.EndDayPart,
            record.ReturnToWorkDate,
            totalDays,
            record.EvidenceStatus,
            record.EvidenceNotes,
            request.Notes,
            now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new SicknessUpdatedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            record.CategoryId,
            record.StartDate,
            record.EndDate,
            record.TotalDays,
            now), cancellationToken);

        return Result.Success(new UpdateSicknessRecordResponse(
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
