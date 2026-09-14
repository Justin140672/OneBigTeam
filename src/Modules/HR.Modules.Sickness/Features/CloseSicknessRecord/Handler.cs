using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-close the record or double-raise a
        // return-to-work review.
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CloseSicknessRecordResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CloseSicknessRecordResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var beforeCategoryId = record.CategoryId;
        var beforeStartDate = record.StartDate;
        var beforeEndDate = record.EndDate;
        var beforeTotalDays = record.TotalDays;

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        // Mandatory, always set (no opt-out) — see CompanySettings.ReturnToWorkRequiredAfterDays.
        //
        // SICK-05: `totalDays` here is a *working-day* count (SicknessCalculator), not calendar
        // days. That is intentional for this specific threshold — "how many days were actually
        // missed" is a defensible basis for deciding whether a return-to-work chat is warranted —
        // and must not be confused with the fit-note threshold above, which is deliberately
        // evaluated in calendar days (see FitNoteEvaluator's doc comment). Do not swap TotalDays
        // for a calendar-day calculation here without updating the decision record in
        // specifications/product-specifications/00-current-product-decisions.md.
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

        // Built from in-memory values ahead of the save, so it can double as both the response
        // and the payload persisted for an idempotency replay.
        var response = new CloseSicknessRecordResponse(
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
            record.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                // Lost a race against a concurrent duplicate under the same key. SaveIdempotentAsync
                // already rolled back this attempt's transaction - nothing here was committed, so
                // skip the rest of this handler's side effects and hand back the winner's result.
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // One-time evaluation at close time (SICK-01) — an absence that already reached the
        // fit-note threshold by the time it was closed (including one closed before the daily
        // FitNoteRequestJob last ran) gets its evidence request immediately rather than waiting for
        // the next job run.
        await fitNoteEvidenceRequestService.RequestIfEligibleAsync(
            record, sicknessSettings.FitNoteRequiredAfterDays, request.EndDate, now, cancellationToken,
            evaluationDateIsFinal: true);

        await auditPublisher.PublishAsync(new SicknessClosedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            request.ActorEmployeeId,
            beforeCategoryId,
            beforeStartDate,
            beforeEndDate,
            beforeTotalDays,
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
                request.ActorEmployeeId,
                returnToWorkReview.DueDate,
                now), cancellationToken);
        }

        return Result.Success(response);
    }
}
