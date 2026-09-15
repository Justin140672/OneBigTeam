using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

// Extracted from ProcessLeavingEmployeesJob so the exact same finalisation steps (status
// transition, conditional access disabling, offboarding-completeness check, manager notification,
// audit publish, integration event publish) run whether triggered by the daily job reaching a due
// LeavingDate, or by HR confirming a backdated LeavingDate via Start/AmendLeavingProcess. Both
// Employee/EmployeeLeavingProcess guard their own state transitions (Complete throws unless
// InProgress), which is what keeps repeated calls for the same process safe.
internal sealed class EmployeeDepartureFinalizer(
    EmployeesDbContext dbContext,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    IOffboardingStatusReader offboardingStatusReader,
    ICompanyLeavingSettingsReader leavingSettingsReader,
    INotificationWriter notificationWriter,
    IEmployeeTimelineWriter timelineWriter,
    IDirectReportsReader directReportsReader) : IEmployeeDepartureFinalizer
{
    public async Task FinalizeAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Already fully finalised (terminal state persisted AND every downstream step below
        // completed) — a safe no-op if invoked again defensively.
        if (process.FinalisationCompletedAt is not null)
            return;

        // If the process is already Completed but FinalisationCompletedAt is still null, a prior
        // attempt got as far as persisting the terminal state but crashed/threw before finishing
        // the downstream steps below (see ProcessLeavingEmployeesJob's reconciliation scan, which
        // is what re-invokes FinalizeAsync in that situation). Re-running PersistTerminalStateAsync
        // in that case would throw (Complete()/SetFormerEmployee guard against a second real
        // transition), so instead recompute accessDisabled from the already-persisted
        // Employee.HasSystemAccess and go straight to the downstream steps.
        var accessDisabled = process.Status == LeavingProcessStatus.Completed
            ? !employee.HasSystemAccess
            : await PersistTerminalStateAsync(employee, process, now, cancellationToken);

        await CompleteDownstreamFinalisationAsync(employee, process, now, accessDisabled, cancellationToken);
    }

    // Persists the actual departure transition: manager-cascade reassignment, access disabling,
    // Employee -> FormerEmployee, and EmployeeLeavingProcess -> Completed.
    private async Task<bool> PersistTerminalStateAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // OFF-06: if the departing employee was a manager, their direct reports must not silently
        // keep pointing at a former employee — reassign (or clear, pending HR) their ManagerId
        // now, and publish EmployeeManagerChangedIntegrationEvent per report so existing
        // cross-module consumers (Probation's ManagerChangedHandler; any future consumer) keep
        // manager-scoped work correctly assigned. Guarded on each report's current ManagerId still
        // pointing at this employee, so re-finalising (defensive; Complete() already guards against
        // a genuine repeat call) never double-reassigns or republishes for a report already moved.
        //
        // Deliberately run — and saved — BEFORE the employee/process terminal-state mutations
        // below: this shares the same DbContext/unit-of-work as those mutations, so if the cascade
        // save happened afterwards (the original ordering) it would flush the not-yet-fully-
        // processed terminal transition prematurely, before the offboarding-completeness check,
        // notification, audit publish, integration publish and timeline write in
        // CompleteDownstreamFinalisationAsync had run. Reassigning reports doesn't depend on the
        // employee already being marked former, so this ordering is safe.
        await CascadeManagerDepartureAsync(employee, process, now, cancellationToken);

        var accessDisabled = false;
        if (await leavingSettingsReader.GetAutoDisableAccessOnLeavingDateAsync(employee.CompanyId, cancellationToken))
        {
            employee.SetSystemAccess(false, now);
            accessDisabled = true;
        }

        employee.SetFormerEmployee(now);
        process.Complete(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return accessDisabled;
    }

    // Runs the finalisation steps that follow the terminal-state save: offboarding-completeness
    // check, manager notification, audit publish, integration event publish, timeline write, and
    // finally marks the process's FinalisationCompletedAt. May be re-entered on its own (with the
    // terminal state already persisted) if an earlier attempt failed partway through — see
    // FinalizeAsync and ProcessLeavingEmployeesJob's reconciliation scan.
    private async Task CompleteDownstreamFinalisationAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        bool accessDisabled,
        CancellationToken cancellationToken)
    {
        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            employee.CompanyId, employee.Id, cancellationToken);
        var offboardingIncomplete = offboardingStatus is null || offboardingStatus.Status != "Completed";

        if (offboardingIncomplete && employee.ManagerId.HasValue)
        {
            // Idempotency guard: this step can be re-run for the same process by the reconciliation
            // scan above, so check for an existing notification (matched by employee/source-entity/
            // type) rather than relying on Guid.NewGuid() uniqueness, which would double-send it.
            var alreadyNotified = await notificationWriter.ExistsAsync(
                employee.ManagerId.Value, employee.Id, NotificationType.IncompleteOffboardingAtDeparture, cancellationToken);

            if (!alreadyNotified)
            {
                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    employee.CompanyId,
                    employee.ManagerId.Value,
                    "Offboarding incomplete at departure",
                    $"{employee.FirstName} {employee.LastName} has left the company but has outstanding offboarding tasks.",
                    employee.Id,
                    NotificationType.IncompleteOffboardingAtDeparture,
                    NotificationPriority.High,
                    now,
                    cancellationToken);
            }
        }

        // Audit publishing and integration-event publishing have no equivalent existence check
        // available (unlike the notification above), so a retry that reaches this point a second
        // time will republish both. That is an accepted, deliberately simple trade-off rather than
        // building a general outbox/dedupe mechanism for every downstream consumer — this only
        // happens on the narrow "terminal state persisted but downstream steps not fully done" retry
        // path (FinalisationCompletedAt still null); a fully-finalised process short-circuits at the
        // top of FinalizeAsync and never re-publishes. The one known consumer
        // (MarkOffboardingIncompleteOnDepartureFinalisedHandler) is itself documented idempotent for
        // redelivery.
        await auditEventPublisher.PublishAsync(
            new EmployeeDepartureFinalisedAuditEvent(
                employee.CompanyId,
                employee.Id,
                process.Id,
                now,
                accessDisabled,
                offboardingIncomplete),
            cancellationToken);

        // Cross-module notification so consuming modules (e.g. Leave) can stop treating this
        // employee as active — e.g. no new policy-year balance/carry-over should be generated for
        // them from this point on. Published after the audit event, mirroring the ordering used
        // elsewhere in this codebase (state change -> audit -> integration event).
        await integrationEventPublisher.PublishAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(
                employee.CompanyId,
                employee.Id,
                process.LeavingDate,
                now,
                accessDisabled),
            cancellationToken);

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                employee.CompanyId,
                employee.Id,
                DateOnly.FromDateTime(now.DateTime),
                EmployeeTimelineEventType.EmploymentEnded,
                EmployeeTimelineCategory.Employment,
                "Employment ended",
                $"{employee.FirstName} {employee.LastName}'s employment ended.",
                performedByUserId: null,
                "Employees",
                process.Id,
                EmployeeTimelineVisibility.AuthorisedInternal,
                now),
            cancellationToken);

        process.MarkFinalisationCompleted(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // OFF-06: reassigns every direct report currently pointing at the departing employee to
    // process.ReplacementManagerEmployeeId (or clears ManagerId if none was nominated), and
    // publishes EmployeeManagerChangedIntegrationEvent for each. This is the only place a
    // manager's own departure cascades a manager change to their reports — a direct edit to an
    // employee's ManagerId field still goes through AssignManager/UpdateEmploymentDetails, which
    // already publish this same event for that scenario.
    private async Task CascadeManagerDepartureAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
            employee.CompanyId, employee.Id, cancellationToken);

        if (directReportIds.Count == 0)
            return;

        var reports = await dbContext.Employees
            .Where(e => e.CompanyId == employee.CompanyId && directReportIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var replacementManagerId = process.ReplacementManagerEmployeeId;
        var reassignedReports = new List<Employee>();
        var pendingEvents = new List<PendingManagerChangedEvent>();

        foreach (var report in reports)
        {
            // Idempotency guard: only act on reports still pointing at the departing employee —
            // relevant if this is somehow invoked twice for the same process (Complete() already
            // guards against that at the process level, but this keeps the cascade itself safe on
            // its own terms too).
            if (report.ManagerId != employee.Id)
                continue;

            report.Assign(report.DepartmentId, report.PositionProfileId, report.LocationId, replacementManagerId, now);
            reassignedReports.Add(report);

            // Durability fix: capture the previous/new manager values now, in the SAME save as the
            // reassignment below, rather than relying on publishing immediately afterwards. If the
            // process is interrupted between the save and publish, report.ManagerId already points
            // at replacementManagerId, so the "still pointing at the departing employee" guard above
            // would skip this report entirely on any retry — silently losing the event. This record
            // survives that gap and is delivered by ReconcilePendingManagerChangedEventsJob.
            pendingEvents.Add(PendingManagerChangedEvent.Create(
                Guid.NewGuid(), employee.CompanyId, report.Id, employee.Id, replacementManagerId,
                process.Id, now, now));
        }

        if (reassignedReports.Count == 0)
            return;

        dbContext.PendingManagerChangedEvents.AddRange(pendingEvents);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var pendingEvent in pendingEvents)
        {
            // Gap-1 reliability fix: PublishAsync always returns successfully even when a required
            // consumer (Probation's ManagerChangedHandler) throws, because IntegrationEventPublisher
            // deliberately swallows and only logs each handler's own exception. Using
            // PublishAndConfirmAsync instead means PublishedAt is only set once every handler marked
            // IRequiredIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent> has actually
            // succeeded — otherwise the record is left unpublished for
            // ReconcilePendingManagerChangedEventsJob to retry. Redelivery is safe: Probation's
            // handler is idempotent (no-op if ManagerEmployeeId already matches), and the other
            // (non-required) consumer, Employees' own CreateTimelineEntryOnManagerChanged, dedupes
            // via EmployeeTimelineWriter.TryAddAsync.
            var confirmed = await integrationEventPublisher.PublishAndConfirmAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    pendingEvent.CompanyId, pendingEvent.ReportEmployeeId, pendingEvent.PreviousManagerId,
                    pendingEvent.NewManagerId, pendingEvent.OccurredAt),
                cancellationToken);

            if (confirmed)
                pendingEvent.MarkPublished(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
