using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// Creates a fit-note evidence request — and, via SicknessEvidenceRequestedIntegrationEvent, the
/// corresponding "Upload fit note" task in the Tasks module (see
/// HR.Modules.Tasks.Features.SicknessEvidenceRequested.SicknessEvidenceRequestedHandler) — the first
/// time a sickness record's calendar-day duration reaches the company's configured
/// FitNoteRequiredAfterDays threshold (SICK-01).
///
/// Idempotent by design: before creating anything it checks whether a non-cancelled evidence
/// request already exists for the record and skips if so. That single guard is what makes a re-run
/// of the daily FitNoteRequestJob — including a Hangfire retry after a partial failure — a safe
/// no-op: no duplicate requests, tasks, notifications or audit events. Records whose evidence has
/// already been Received or Waived are never re-requested, even on rerun.
///
/// Used both for the immediate, one-time evaluation performed at record creation and at closure (so
/// a backdated or already-over-threshold absence is caught straight away rather than waiting for the
/// next daily run) and by FitNoteRequestJob's daily re-evaluation of open records plus its catch-all
/// pass over closed records that don't yet have a request.
/// </summary>
internal sealed class FitNoteEvidenceRequestService(
    SicknessDbContext db,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher,
    ITaskRescheduler taskRescheduler)
{
    internal static readonly Guid SystemActorId = Guid.Empty;
    private const int DueDateDaysFromNow = 7;

    /// <summary>
    /// Evaluates <paramref name="record"/> against <paramref name="evaluationDate"/> (today, for an
    /// ongoing absence, or the absence's own end date once closed) and creates a
    /// <see cref="SicknessEvidenceRequest"/> plus its downstream events if the calendar-day
    /// threshold has been reached and no live request already exists. Saves changes itself.
    /// Returns true if a request was created.
    /// </summary>
    public async Task<bool> RequestIfEligibleAsync(
        SicknessRecord record,
        int fitNoteRequiredAfterDays,
        DateOnly evaluationDate,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        // Set only when evaluationDate is the absence's authoritative (closed) end date rather
        // than "today". An existing pending request created while the record was still open is
        // anchored on the day it was raised; once the real end date is known, its deadline (and
        // the linked Upload task) is corrected to endDate + 7.
        bool evaluationDateIsFinal = false)
    {
        // Received or waived evidence permanently suppresses further requests for this record.
        if (record.EvidenceStatus == SicknessEvidenceStatus.Received ||
            record.EvidenceStatus == SicknessEvidenceStatus.Waived)
            return false;

        if (!FitNoteEvaluator.IsThresholdReached(record.StartDate, evaluationDate, fitNoteRequiredAfterDays))
            return false;

        var existingRequest = await db.SicknessEvidenceRequests
            .SingleOrDefaultAsync(
                e => e.SicknessRecordId == record.Id && e.Status != SicknessEvidenceRequestStatus.Cancelled,
                cancellationToken);

        if (existingRequest is not null)
        {
            if (evaluationDateIsFinal)
            {
                var correctedDueDate = evaluationDate.AddDays(DueDateDaysFromNow);
                if (existingRequest.Reschedule(correctedDueDate, now))
                {
                    await db.SaveChangesAsync(cancellationToken);
                    await taskRescheduler.RescheduleManyBySourceEntitiesAsync(
                        record.CompanyId,
                        [existingRequest.Id],
                        TaskSource.Sickness,
                        TaskActionType.Upload,
                        correctedDueDate,
                        cancellationToken);
                }
            }

            return false;
        }

        var dueDate = evaluationDate.AddDays(DueDateDaysFromNow);

        var request = SicknessEvidenceRequest.Create(
            Guid.NewGuid(),
            record.CompanyId,
            record.Id,
            SystemActorId,
            dueDate,
            null,
            now);

        db.SicknessEvidenceRequests.Add(request);

        if (record.EvidenceStatus != SicknessEvidenceStatus.Pending)
            record.MarkEvidencePending(now);

        await db.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            new SicknessEvidenceRequestedIntegrationEvent(
                CompanyId:         record.CompanyId,
                EmployeeId:        record.EmployeeId,
                SicknessRecordId:  record.Id,
                EvidenceRequestId: request.Id,
                DueDate:           dueDate,
                OccurredAt:        now),
            cancellationToken);

        await auditPublisher.PublishAsync(
            new SicknessEvidenceRequestedAuditEvent(
                EvidenceRequestId: request.Id,
                SicknessRecordId:  record.Id,
                CompanyId:         record.CompanyId,
                EmployeeId:        record.EmployeeId,
                ActorId:           SystemActorId,
                DueDate:           dueDate,
                OccurredAt:        now),
            cancellationToken);

        return true;
    }
}
