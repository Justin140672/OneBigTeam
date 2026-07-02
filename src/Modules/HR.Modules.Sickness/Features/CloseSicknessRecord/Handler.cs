using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CloseSicknessRecord;

internal sealed class CloseSicknessRecordHandler(
    SicknessDbContext db,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IPublicHolidayReader publicHolidayReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CloseSicknessRecordResponse>> HandleAsync(
        CloseSicknessRecordRequest request,
        CancellationToken cancellationToken)
    {
        var record = await db.SicknessRecords
            .FirstOrDefaultAsync(r =>
                r.Id == request.Id &&
                r.CompanyId == request.CompanyId &&
                r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure<CloseSicknessRecordResponse>(Error.NotFound("Sickness record not found."));

        if (record.EndDate.HasValue)
            return Result.Failure<CloseSicknessRecordResponse>(Error.Conflict("Sickness record is already closed."));

        if (request.EndDate < record.StartDate)
            return Result.Failure<CloseSicknessRecordResponse>(Error.Validation("EndDate must be on or after StartDate."));

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var sicknessSettings = await sicknessSettingsReader.GetSicknessSettingsAsync(
            request.CompanyId, cancellationToken);

        IReadOnlyCollection<DateOnly>? publicHolidays = null;
        if (sicknessSettings.ExcludePublicHolidaysFromSickness)
        {
            var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                request.CompanyId, record.StartDate, request.EndDate, cancellationToken);
            publicHolidays = holidays.Select(h => h.Date).ToList();
        }

        var totalDays = SicknessCalculator.CalculateTotalDays(
            record.StartDate, record.StartDayPart,
            request.EndDate, request.EndDayPart,
            workingPattern, publicHolidays);

        var updatedEvidenceStatus = FitNoteEvaluator.EvaluateOnClose(
            record.EvidenceStatus,
            sicknessSettings.FitNoteRequiredAfterDays,
            totalDays);

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        record.Close(
            request.EndDate,
            request.EndDayPart,
            request.ReturnToWorkDate,
            totalDays,
            updatedEvidenceStatus,
            record.EvidenceNotes,
            now);

        if (request.Notes is not null)
        {
            record.Update(
                record.CategoryId,
                record.StartDate,
                record.StartDayPart,
                record.EndDate,
                record.EndDayPart,
                record.ReturnToWorkDate,
                record.TotalDays,
                record.EvidenceStatus,
                record.EvidenceNotes,
                request.Notes,
                now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new SicknessClosedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            record.CategoryId,
            record.StartDate,
            record.EndDate!.Value,
            record.TotalDays,
            now), cancellationToken);

        return Result.Success(new CloseSicknessRecordResponse(
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
