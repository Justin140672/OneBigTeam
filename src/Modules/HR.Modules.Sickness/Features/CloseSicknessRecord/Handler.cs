using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CloseSicknessRecord;

internal sealed class CloseSicknessRecordHandler(
    SicknessDbContext db,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IPublicHolidayReader publicHolidayReader,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher eventPublisher,
    FitNoteEvidenceRequestService fitNoteEvidenceRequestService)
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
            record.StartDate,
            request.EndDate);

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        // Mandatory, always set (no opt-out) — see CompanySettings.ReturnToWorkRequiredAfterDays.
        ReturnToWorkReview? returnToWorkReview = null;
        if (totalDays >= sicknessSettings.ReturnToWorkRequiredAfterDays)
        {
            var dueDate = request.ReturnToWorkDate ?? request.EndDate;

            returnToWorkReview = ReturnToWorkReview.Create(
                Guid.NewGuid(),
                record.CompanyId,
                record.Id,
                record.EmployeeId,
                dueDate,
                now);

            db.ReturnToWorkReviews.Add(returnToWorkReview);
        }

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

        // One-time evaluation at close time (SICK-01) — an absence that already reached the
        // fit-note threshold by the time it was closed (including one closed before the daily
        // FitNoteRequestJob last ran) gets its evidence request immediately rather than waiting for
        // the next job run.
        await fitNoteEvidenceRequestService.RequestIfEligibleAsync(
            record, sicknessSettings.FitNoteRequiredAfterDays, request.EndDate, now, cancellationToken);

        await auditPublisher.PublishAsync(new SicknessClosedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            record.CategoryId,
            record.StartDate,
            record.EndDate!.Value,
            record.TotalDays,
            now), cancellationToken);

        if (returnToWorkReview is not null)
        {
            await eventPublisher.PublishAsync(new ReturnToWorkReviewRequiredIntegrationEvent(
                returnToWorkReview.CompanyId,
                returnToWorkReview.EmployeeId,
                returnToWorkReview.SicknessRecordId,
                returnToWorkReview.Id,
                returnToWorkReview.DueDate,
                now), cancellationToken);

            await auditPublisher.PublishAsync(new ReturnToWorkReviewRequiredAuditEvent(
                returnToWorkReview.Id,
                returnToWorkReview.SicknessRecordId,
                returnToWorkReview.CompanyId,
                returnToWorkReview.EmployeeId,
                returnToWorkReview.DueDate,
                now), cancellationToken);
        }

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
