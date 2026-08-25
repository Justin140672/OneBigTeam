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
/// Idempotent against duplicate event delivery: if the record's <c>ManagerEmployeeId</c> already
/// equals the event's <c>NewManagerId</c>, the change has already been applied and this is a no-op —
/// so a redelivered event never cancels/recreates the same task twice.
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
    IEmployeeNameReader employeeNameReader,
    IEmployeeProbationDatesReader probationDatesReader,
    ICompanyTimeZoneReader timeZoneReader,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILogger<ManagerChangedHandler> logger) : IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
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

        if (record.ManagerEmployeeId == integrationEvent.NewManagerId)
            return; // Already applied — duplicate delivery of the same event.

        var pendingCheckIn = await dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == integrationEvent.CompanyId
                     && r.ProbationRecordId == record.Id
                     && r.ReviewType == ProbationReviewType.ManagerCheckIn
                     && r.Status == ProbationReviewStatus.Pending,
                cancellationToken);

        var now = clock.UtcNowOffset();

        if (integrationEvent.NewManagerId is null)
        {
            if (pendingCheckIn is not null)
            {
                await taskCanceller.CancelBySourceEntityAsync(
                    record.CompanyId, pendingCheckIn.Id, TaskSource.Probation, TaskActionType.Review, cancellationToken);
            }

            logger.LogWarning(
                "Probation record {ProbationRecordId} employee {EmployeeId} lost their manager; " +
                "ManagerCheckIn task cancelled and left unassigned pending a new manager.",
                record.Id, record.EmployeeId);

            return;
        }

        record.ChangeManager(integrationEvent.NewManagerId.Value, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (pendingCheckIn is null)
            return;

        // The Tasks module has no cross-module "reassign" contract — the ITaskCreator/ITaskCanceller
        // pair already used throughout this module (see ProbationReviewRecalculationService,
        // ProbationExtensionService) is the established pattern: cancel the task pointed at the old
        // manager and create a fresh one, against the same sourceEntityId, for the new manager.
        await taskCanceller.CancelBySourceEntityAsync(
            record.CompanyId, pendingCheckIn.Id, TaskSource.Probation, TaskActionType.Review, cancellationToken);

        var names = await employeeNameReader.GetNamesAsync(record.CompanyId, [record.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(record.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            record.CompanyId,
            integrationEvent.NewManagerId.Value,
            $"Complete probation review — {employeeName}",
            $"Probation manager check-in due {pendingCheckIn.DueDate:d MMM yyyy} (reassigned).",
            TaskPriority.High,
            TaskSource.Probation,
            TaskActionType.Review,
            pendingCheckIn.DueDate,
            assignedEmployeeId: integrationEvent.NewManagerId.Value,
            assignedUserId: integrationEvent.NewManagerId.Value,
            sourceEntityId: pendingCheckIn.Id,
            cancellationToken);
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
