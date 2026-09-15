using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Modules.Companies.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Probation.Features.ReassignReviewsOnManagerChanged;

/// <summary>
/// PROB-04: keeps ManagerCheckIn tasks pointed at the employee's current responsible manager when
/// that manager changes mid-probation. Consumes <c>EmployeeManagerChangedIntegrationEvent</c>
/// (published by the Employees module's AssignManager/UpdateEmploymentDetails handlers) — the first
/// cross-module consumer of that event; scoped strictly to Probation's own tasks, not a
/// general-purpose "manager changed reassigns everything" mechanism (that is out of scope for this
/// ticket).
///
/// Only acts on the employee's own probation record (i.e. when <c>EmployeeId</c> on the event
/// matches a probation record's <c>EmployeeId</c>) — a manager's own probation record, if they have
/// one, is unaffected by someone else's manager reassignment.
///
/// Round 3 reliability fix: completion is now judged by the ACTUAL persisted task state, not by
/// "does record.ManagerEmployeeId already equal the event's NewManagerId". The old equality guard
/// returned immediately on that condition, which meant a retry after a partial failure (manager
/// saved, then task cancel/create threw) saw "already correct" and skipped the task work entirely —
/// so PublishAndConfirmAsync would mark the event delivered even though the review task was left
/// pointing at the old/departed manager. ReconcileManagerCheckInTaskAsync below always re-checks
/// current task state and only returns once a correctly-assigned single active task exists (or none
/// is required), regardless of whether the manager field needed updating this time.
///
/// Duplicate-task prevention: before creating a replacement task, the handler checks for an
/// existing open task for the same source entity/assignee via
/// <see cref="IOpenTaskBySourceEntityReader"/>, and additionally passes a deterministic
/// <c>idempotencyKey</c> to <see cref="ITaskCreator.CreateAsync"/> so a concurrent/duplicate retry
/// can never create two active tasks for the same reconciliation.
///
/// Out-of-order/delayed event handling: the manager actually applied to the record — and therefore
/// the target of task reconciliation — is resolved via <see cref="ProbationRecord.ApplyManagerChangeFromEvent"/>,
/// which ignores a stale event (one whose OccurredAt is older than the last manager-change event
/// already applied). Recovery/redelivery of an old A-&gt;B event after a later B-&gt;C event has landed
/// reconciles the task against the CURRENT (C) manager, never regressing to the stale payload.
///
/// Missing-manager handling: if the employee is left without a manager (<c>NewManagerId</c> is
/// null), any open ManagerCheckIn task is cancelled rather than left pointing at a manager who is no
/// longer responsible for the employee. The probation record's <c>ManagerEmployeeId</c> is a
/// non-nullable field (mirroring the domain's "every probation record has a responsible manager"
/// invariant), so it is left at its last-known value rather than cleared; the cancelled task means
/// nobody is currently working the check-in until a new manager is assigned, which will redeliver
/// this handler and create a fresh task.
/// </summary>
internal sealed class ManagerChangedHandler(
    ProbationDbContext dbContext,
    ITaskCreator taskCreator,
    ITaskCanceller taskCanceller,
    IOpenTaskBySourceEntityReader openTaskReader,
    IEmployeeNameReader employeeNameReader,
    IEmployeeProbationDatesReader probationDatesReader,
    ICompanyTimeZoneReader timeZoneReader,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILogger<ManagerChangedHandler> logger) : IRequiredIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeManagerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var record = await dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == integrationEvent.CompanyId
                     && r.EmployeeId == integrationEvent.EmployeeId
                     && (r.Status == ProbationStatus.Active
                         || r.Status == ProbationStatus.ReviewDue
                         || r.Status == ProbationStatus.Extended
                         || r.Status == ProbationStatus.NotStarted),
                cancellationToken);

        if (record is null)
        {
            // PROB-06: no in-flight record. Either none exists at all yet — most likely because
            // creation was originally deferred for lack of a manager (see EmployeeCreatedHandler)
            // and one is only being assigned now — or a record exists but has already reached a
            // decided/terminal status (Passed/Failed/NotApplicable), which must never be
            // resurrected by a later manager change. Distinguish the two with a second query scoped
            // to "any status at all" before attempting deferred creation.
            var anyRecordExists = await dbContext.ProbationRecords
                .AnyAsync(
                    r => r.CompanyId == integrationEvent.CompanyId && r.EmployeeId == integrationEvent.EmployeeId,
                    cancellationToken);

            if (!anyRecordExists && integrationEvent.NewManagerId is not null)
                await TryCreateDeferredRecordAsync(integrationEvent, cancellationToken);

            return;
        }

        var now = clock.UtcNowOffset();

        var pendingCheckIn = await dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == integrationEvent.CompanyId
                     && r.ProbationRecordId == record.Id
                     && r.ReviewType == ProbationReviewType.ManagerCheckIn
                     && r.Status == ProbationReviewStatus.Pending,
                cancellationToken);

        if (integrationEvent.NewManagerId is null)
        {
            if (pendingCheckIn is not null)
            {
                // Idempotent: CancelBySourceEntityAsync is itself a no-op if no matching open task
                // exists, so this is safe to run every time regardless of whether an earlier attempt
                // already cancelled it.
                await taskCanceller.CancelBySourceEntityAsync(
                    record.CompanyId, pendingCheckIn.Id, TaskSource.Probation, TaskActionType.Review, cancellationToken);

                logger.LogWarning(
                    "Probation record {ProbationRecordId} employee {EmployeeId} lost their manager; " +
                    "ManagerCheckIn task cancelled and left unassigned pending a new manager.",
                    record.Id, record.EmployeeId);
            }

            return;
        }

        // Resolves against ManagerChangeSourceOccurredAt precedence — a stale/out-of-order event is
        // a no-op here, and record.ManagerEmployeeId (read afterwards) reflects whichever manager
        // change actually "wins", not necessarily this event's own payload.
        var recordChanged = record.ApplyManagerChangeFromEvent(
            integrationEvent.NewManagerId.Value, integrationEvent.OccurredAt, now);

        if (recordChanged)
        {
            // Ticket 16 (optimistic concurrency) classification: purely integration-event/system-
            // driven (fired from the Employees module's manager-changed event), not a client-loaded
            // edit form — the shared VersionAdvancingSaveChangesInterceptor advances Version
            // automatically here.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Always reconciled — even when the manager field required no change this time — so a retry
        // that previously failed after the manager save but before the task was fixed up still
        // completes the task work instead of short-circuiting on a stale "already applied" read.
        await ReconcileManagerCheckInTaskAsync(record, pendingCheckIn, cancellationToken);
    }

    /// <summary>
    /// Ensures exactly one active ManagerCheckIn task exists for <paramref name="pendingCheckIn"/>,
    /// correctly assigned to <c>record.ManagerEmployeeId</c> (the CURRENT persisted manager, not
    /// necessarily this event's payload manager — see <see cref="ProbationRecord.ApplyManagerChangeFromEvent"/>).
    /// Safe to call repeatedly/concurrently: checks the actually-open task via
    /// <see cref="IOpenTaskBySourceEntityReader"/> first (idempotent no-op if already correct), and
    /// the replacement creation carries a deterministic idempotency key as a second, database-backed
    /// guard against duplicates.
    /// </summary>
    private async Task ReconcileManagerCheckInTaskAsync(
        ProbationRecord record, ProbationReview? pendingCheckIn, CancellationToken cancellationToken)
    {
        if (pendingCheckIn is null)
            return; // No in-flight ManagerCheckIn review — nothing to reconcile.

        var targetManagerId = record.ManagerEmployeeId;

        var existingCorrectTaskId = await openTaskReader.GetOpenTaskIdForAssigneeAsync(
            record.CompanyId, pendingCheckIn.Id, targetManagerId, TaskActionType.Review, cancellationToken);

        if (existingCorrectTaskId is not null)
            return; // Already correctly assigned — nothing further to do (covers duplicate redelivery).

        // The Tasks module has no cross-module "reassign" contract — the ITaskCreator/ITaskCanceller
        // pair already used throughout this module (see ProbationReviewRecalculationService,
        // ProbationExtensionService) is the established pattern: cancel any task still pointed at a
        // stale assignee and create a fresh one, against the same sourceEntityId, for the current
        // manager. CancelBySourceEntityAsync only cancels the first open match for this source
        // entity/action type, which is correct here — ManagerCheckIn reviews only ever have at most
        // one active task at a time by construction.
        await taskCanceller.CancelBySourceEntityAsync(
            record.CompanyId, pendingCheckIn.Id, TaskSource.Probation, TaskActionType.Review, cancellationToken);

        var names = await employeeNameReader.GetNamesAsync(record.CompanyId, [record.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(record.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            record.CompanyId,
            targetManagerId,
            $"Complete probation review — {employeeName}",
            $"Probation manager check-in due {pendingCheckIn.DueDate:d MMM yyyy} (reassigned).",
            TaskPriority.High,
            TaskSource.Probation,
            TaskActionType.Review,
            pendingCheckIn.DueDate,
            assignedEmployeeId: targetManagerId,
            assignedUserId: targetManagerId,
            sourceEntityId: pendingCheckIn.Id,
            cancellationToken,
            idempotencyKey: $"ProbationManagerCheckIn:{pendingCheckIn.Id}:{targetManagerId}");
    }

    /// <summary>
    /// PROB-06: completes the "manager assigned later" deferral described in
    /// EmployeeCreatedHandler. Only reachable when no probation record of any status exists yet for
    /// this employee, so this is inherently idempotent against redelivery of the same manager-change
    /// event — a second delivery finds the just-created record and takes the ordinary
    /// already-applied/reassignment path above instead. Requires a resolvable ProbationEndDate (the
    /// Employees module always sets one via ProbationDateResolver at employee creation, so a null
    /// here would indicate a genuinely unusual employee record); without one there is no probation
    /// period to initialise, so the record is left deferred rather than guessed at.
    /// </summary>
    private async Task TryCreateDeferredRecordAsync(
        EmployeeManagerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var dates = await probationDatesReader.GetProbationDatesAsync(
            integrationEvent.CompanyId, integrationEvent.EmployeeId, cancellationToken);

        if (dates?.ProbationEndDate is null)
            return;

        var now = clock.UtcNowOffset();
        var timeZoneId = await timeZoneReader.GetTimeZoneAsync(integrationEvent.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);

        var newRecord = ProbationRecord.Create(
            Guid.NewGuid(),
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            integrationEvent.NewManagerId!.Value,
            dates.StartDate,
            dates.ProbationEndDate.Value,
            notes: null,
            today,
            now);

        dbContext.ProbationRecords.Add(newRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        // PROB-07: system-generated deferred creation — actor is ProbationSystemActor.Id, distinct
        // from a human directly creating a record via CreateProbationRecordHandler.
        await auditPublisher.PublishAsync(new ProbationRecordCreatedAuditEvent(
            newRecord.CompanyId,
            newRecord.Id,
            newRecord.EmployeeId,
            newRecord.ManagerEmployeeId,
            ProbationSystemActor.Id,
            newRecord.StartDate,
            newRecord.ExpectedEndDate,
            HasNotes: false,
            now), cancellationToken);

        logger.LogInformation(
            "Probation record {ProbationRecordId} created for employee {EmployeeId} on manager assignment " +
            "(creation was previously deferred for lack of a manager).",
            newRecord.Id, newRecord.EmployeeId);
    }
}
