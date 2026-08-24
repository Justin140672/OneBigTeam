using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;

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
    IEmployeeTimelineWriter timelineWriter) : IEmployeeDepartureFinalizer
{
    public async Task FinalizeAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        employee.SetFormerEmployee(now);
        process.Complete(now);

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
}
