using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class RecordMySicknessHandler(
    SicknessDbContext db,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IPublicHolidayReader publicHolidayReader,
    IAuditEventPublisher auditPublisher,
    IManagerReader managerReader,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter)
{
    public async Task<Result<RecordSicknessResponse>> HandleAsync(
        RecordMySicknessRequest request,
        CancellationToken cancellationToken)
    {
        var categoryExists = await db.SicknessCategories
            .AnyAsync(c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!categoryExists)
            return Result.Failure<RecordSicknessResponse>(Error.NotFound("Sickness category not found."));

        var hasOpenRecord = await db.SicknessRecords
            .AnyAsync(r => r.EmployeeId == request.EmployeeId && r.CompanyId == request.CompanyId && r.EndDate == null, cancellationToken);

        if (hasOpenRecord)
            return Result.Failure<RecordSicknessResponse>(Error.Conflict("Employee already has an open sickness record."));

        decimal? totalDays = null;
        var sicknessSettings = await sicknessSettingsReader.GetSicknessSettingsAsync(
            request.CompanyId, cancellationToken);

        if (request.EndDate.HasValue)
        {
            var endDayPart = request.EndDayPart ?? SicknessDayPart.FullDay;
            var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
                request.CompanyId, request.EmployeeId, cancellationToken);

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

        var evidenceStatus = FitNoteEvaluator.EvaluateOnCreate(sicknessSettings.FitNoteRequiredAfterDays, totalDays);

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
            evidenceStatus,
            now);

        db.SicknessRecords.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var managerId = await managerReader.GetManagerIdAsync(entity.CompanyId, entity.EmployeeId, cancellationToken);
        if (managerId.HasValue)
        {
            var names = await employeeNameReader.GetNamesAsync(entity.CompanyId, [entity.EmployeeId], cancellationToken);
            var employeeName = names.GetValueOrDefault(entity.EmployeeId, "Unknown Employee");

            await notificationWriter.WriteAsync(
                Guid.NewGuid(), entity.CompanyId, managerId.Value,
                $"Sickness recorded — {employeeName}",
                $"{employeeName} has been recorded as sick from {entity.StartDate:d MMM yyyy}.",
                entity.Id,
                NotificationType.SicknessRecorded,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        await auditPublisher.PublishAsync(new SicknessRecordedAuditEvent(
            entity.CompanyId,
            entity.EmployeeId,
            entity.Id,
            entity.CategoryId,
            entity.StartDate,
            entity.EndDate,
            entity.TotalDays,
            now), cancellationToken);

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
