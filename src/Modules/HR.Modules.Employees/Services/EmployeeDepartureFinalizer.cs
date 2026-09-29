using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

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
        if (process.FinalisationCompletedAt is not null)
            return;

        var accessDisabled = process.Status == LeavingProcessStatus.Completed
            ? !employee.HasSystemAccess
            : await PersistTerminalStateAsync(employee, process, now, cancellationToken);

        await CompleteDownstreamFinalisationAsync(employee, process, now, accessDisabled, cancellationToken);
    }

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
            if (report.ManagerId != employee.Id)
                continue;

            report.Assign(report.DepartmentId, report.PositionProfileId, report.LocationId, replacementManagerId, now);
            reassignedReports.Add(report);

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
