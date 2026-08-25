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
        employee.SetFormerEmployee(now);
        process.Complete(now);

        // OFF-06: if the departing employee was a manager, their direct reports must not silently
        // keep pointing at a former employee — reassign (or clear, pending HR) their ManagerId
        // now, and publish EmployeeManagerChangedIntegrationEvent per report so existing
        // cross-module consumers (Probation's ManagerChangedHandler; any future consumer) keep
        // manager-scoped work correctly assigned. Guarded on each report's current ManagerId still
        // pointing at this employee, so re-finalising (defensive; Complete() already guards against
        // a genuine repeat call) never double-reassigns or republishes for a report already moved.
        await CascadeManagerDepartureAsync(employee, process, now, cancellationToken);

        var accessDisabled = false;
        if (await leavingSettingsReader.GetAutoDisableAccessOnLeavingDateAsync(employee.CompanyId, cancellationToken))
        {
            employee.SetSystemAccess(false, now);
            accessDisabled = true;
        }

        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            employee.CompanyId, employee.Id, cancellationToken);
        var offboardingIncomplete = offboardingStatus is null || offboardingStatus.Status != "Completed";

        await dbContext.SaveChangesAsync(cancellationToken);

        if (offboardingIncomplete && employee.ManagerId.HasValue)
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
                now),
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
        }

        if (reassignedReports.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var report in reassignedReports)
        {
            await integrationEventPublisher.PublishAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    employee.CompanyId, report.Id, employee.Id, replacementManagerId, now),
                cancellationToken);
        }
    }
}
