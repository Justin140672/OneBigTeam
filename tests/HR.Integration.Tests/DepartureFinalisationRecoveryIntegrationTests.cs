using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DepartureFinalisationRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinalisationRecoveryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }


    private async Task<Guid> SeedEmployeeAsync(
        EmployeesDbContext db, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData referenceData,
        string firstName, string lastName, Guid? managerId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            workEmail: $"{firstName}.{lastName}.{Guid.NewGuid():N}@test.example".ToLowerInvariant(),
            startDate: new DateOnly(2020, 1, 1),
            hasSystemAccess: true,
            dateOfBirth: new DateOnly(1990, 1, 1),
            nationality: "British",
            gender: "Prefer not to say",
            employeeNumber: $"EMP-{Guid.NewGuid():N}",
            employmentTypeId: referenceData.EmploymentTypeId,
            departmentId: referenceData.DepartmentId,
            locationId: referenceData.LocationId,
            positionProfileId: referenceData.PositionProfileId,
            now: now);
        employee.Activate(now);
        if (managerId.HasValue)
            employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, managerId, now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private static EmployeeLeavingProcess CreateInProgressProcess(
        Guid companyId, Guid employeeId, DateOnly leavingDate, DateTimeOffset now, Guid? replacementManagerId = null) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            resignationReceivedDate: leavingDate.AddDays(-30),
            leavingDate: leavingDate,
            lastWorkingDay: leavingDate,
            noticePeriodUnit: NoticePeriodUnit.Weeks,
            noticePeriodLength: 4,
            noticeSource: NoticePeriodSource.Employee,
            leavingReason: LeavingReason.Resignation,
            startedByUserId: Guid.NewGuid(),
            now: now,
            replacementManagerEmployeeId: replacementManagerId);


    [Fact]
    public async Task LeavePolicyDeactivationPipeline_FinalisationThroughRealJob_DeactivatesAssignment()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Leave", "Departing");

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var now = DateTimeOffset.UtcNow;
            var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, isDefault: true, now: now);
            leaveDb.LeavePolicies.Add(policy);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2020, 1, 1), now);
            assignmentId = assignment.Id;
            leaveDb.EmployeeLeavePolicyAssignments.Add(assignment);
            await leaveDb.SaveChangesAsync();

            var process = CreateInProgressProcess(companyId, employeeId, new DateOnly(2026, 1, 1), now);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);
            await employeesDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == employeeId);
            var process = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            var finalizer = scope.ServiceProvider.GetRequiredService<IEmployeeDepartureFinalizer>();
            await finalizer.FinalizeAsync(employee, process, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        Guid deactivationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .SingleAsync(d => d.CompanyId == companyId && d.EmployeeId == employeeId);
            Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
            deactivationId = request.Id;

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignment.IsActive);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<LeavePolicyDeactivationJob>();
            await job.ProcessAsync(deactivationId, companyId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await leaveDb.LeavePolicyDeactivationsOnDeparture.SingleAsync(d => d.Id == deactivationId);
            Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, request.Status);
            Assert.NotNull(request.ProcessedAt);

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.False(assignment.IsActive);
            Assert.NotNull(assignment.DeactivatedAt);
        }
    }

    [Fact]
    public async Task LeavePolicyDeactivationPipeline_ReconciliationSweep_ReEnqueuesStuckPendingRow_AndConverges()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Stuck", "Deactivation");

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var now = DateTimeOffset.UtcNow;
            var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, isDefault: true, now: now);
            leaveDb.LeavePolicies.Add(policy);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2020, 1, 1), now);
            assignmentId = assignment.Id;
            leaveDb.EmployeeLeavePolicyAssignments.Add(assignment);

            var request = LeavePolicyDeactivationOnDeparture.CreatePending(Guid.NewGuid(), companyId, employeeId, now, now);
            leaveDb.LeavePolicyDeactivationsOnDeparture.Add(request);
            await leaveDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcileLeavePolicyDeactivationsJob>();
            await reconcileJob.ExecuteAsync();
        }

        Guid deactivationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .SingleAsync(d => d.CompanyId == companyId && d.EmployeeId == employeeId);
            Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
            deactivationId = request.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<LeavePolicyDeactivationJob>();
            await job.ProcessAsync(deactivationId, companyId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.False(assignment.IsActive);
        }
    }


    [Fact]
    public async Task ManagerCascade_MultipleReports_AllGetPendingEvents_PublishedInline_AndReconciliationIsIdempotent()
    {
        var companyId = Guid.NewGuid();
        Guid managerId, report1Id, report2Id, report3Id, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

            managerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Manager", "Departing");
            report1Id = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Report", "One", managerId);
            report2Id = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Report", "Two", managerId);
            report3Id = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Report", "Three", managerId);

            var now = DateTimeOffset.UtcNow;
            var process = CreateInProgressProcess(companyId, managerId, new DateOnly(2026, 1, 1), now, replacementManagerId: null);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);
            await employeesDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == managerId);
            var process = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            var finalizer = scope.ServiceProvider.GetRequiredService<IEmployeeDepartureFinalizer>();
            await finalizer.FinalizeAsync(employee, process, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        var reportIds = new[] { report1Id, report2Id, report3Id };

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

            var pendingEvents = await employeesDb.PendingManagerChangedEvents
                .Where(e => e.CompanyId == companyId && e.LeavingProcessId == processId)
                .ToListAsync();

            Assert.Equal(3, pendingEvents.Count);
            foreach (var reportId in reportIds)
            {
                var pendingEvent = Assert.Single(pendingEvents, e => e.ReportEmployeeId == reportId);
                Assert.Equal(managerId, pendingEvent.PreviousManagerId);
                Assert.Null(pendingEvent.NewManagerId);
                Assert.NotNull(pendingEvent.PublishedAt);
            }

            foreach (var reportId in reportIds)
            {
                var report = await employeesDb.Employees.SingleAsync(e => e.Id == reportId);
                Assert.Null(report.ManagerId);
            }
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcilePendingManagerChangedEventsJob>();
            await reconcileJob.ExecuteAsync();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var pendingEvents = await employeesDb.PendingManagerChangedEvents
                .Where(e => e.CompanyId == companyId && e.LeavingProcessId == processId)
                .ToListAsync();
            Assert.Equal(3, pendingEvents.Count);
            Assert.All(pendingEvents, e => Assert.NotNull(e.PublishedAt));
        }
    }

    [Fact]
    public async Task ManagerCascade_InterruptedPublish_ReconciliationPublishesTheMissedEvent_AndConsumerSideEffectIsObservable()
    {
        var companyId = Guid.NewGuid();
        Guid managerId, reportId, replacementManagerId, processId, strandedEventId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

            managerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Manager", "Interrupted");
            replacementManagerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Replacement", "Manager");
            reportId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Stranded", "Report");

            var now = DateTimeOffset.UtcNow;

            var report = await employeesDb.Employees.SingleAsync(e => e.Id == reportId);
            report.Assign(report.DepartmentId, report.PositionProfileId, report.LocationId, replacementManagerId, now);

            var process = CreateInProgressProcess(companyId, managerId, new DateOnly(2026, 1, 1), now, replacementManagerId);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);

            var strandedEvent = PendingManagerChangedEvent.Create(
                Guid.NewGuid(), companyId, reportId, managerId, replacementManagerId, processId, now, now);
            strandedEventId = strandedEvent.Id;
            employeesDb.PendingManagerChangedEvents.Add(strandedEvent);

            await employeesDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var before = await employeesDb.PendingManagerChangedEvents.SingleAsync(e => e.Id == strandedEventId);
            Assert.Null(before.PublishedAt);

            var timelineBefore = await employeesDb.EmployeeTimelineEntries
                .Where(t => t.EmployeeId == reportId && t.EventType == EmployeeTimelineEventType.ManagerChanged)
                .ToListAsync();
            Assert.Empty(timelineBefore);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcilePendingManagerChangedEventsJob>();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var after = await employeesDb.PendingManagerChangedEvents.SingleAsync(e => e.Id == strandedEventId);
            Assert.NotNull(after.PublishedAt);

            var timelineAfter = await employeesDb.EmployeeTimelineEntries
                .Where(t => t.EmployeeId == reportId && t.EventType == EmployeeTimelineEventType.ManagerChanged)
                .ToListAsync();
            Assert.Single(timelineAfter);
        }

        DateTimeOffset? publishedAtAfterFirstRun;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            publishedAtAfterFirstRun = (await employeesDb.PendingManagerChangedEvents.SingleAsync(e => e.Id == strandedEventId)).PublishedAt;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcilePendingManagerChangedEventsJob>();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var rows = await employeesDb.PendingManagerChangedEvents.Where(e => e.Id == strandedEventId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(publishedAtAfterFirstRun, rows[0].PublishedAt);

            var timelineAfterSecondRun = await employeesDb.EmployeeTimelineEntries
                .Where(t => t.EmployeeId == reportId && t.EventType == EmployeeTimelineEventType.ManagerChanged)
                .ToListAsync();
            Assert.Single(timelineAfterSecondRun);
        }
    }


    [Fact]
    public async Task StrandedDeparture_CompletedWithoutFinalisationCompletedAt_IsRecoveredByProcessLeavingEmployeesJob()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Stranded", "Departure");

            var now = DateTimeOffset.UtcNow;

            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == employeeId);
            var process = CreateInProgressProcess(companyId, employeeId, new DateOnly(2026, 1, 1), now);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);

            employee.SetFormerEmployee(now);
            process.Complete(now);
            await employeesDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var before = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            Assert.Equal(LeavingProcessStatus.Completed, before.Status);
            Assert.Null(before.FinalisationCompletedAt);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessLeavingEmployeesJob>();
            await job.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var after = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            Assert.NotNull(after.FinalisationCompletedAt);
        }

        DateTimeOffset? finalisationCompletedAtAfterFirstRun;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            finalisationCompletedAtAfterFirstRun =
                (await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId)).FinalisationCompletedAt;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessLeavingEmployeesJob>();
            await job.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var afterSecondRun = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            Assert.Equal(finalisationCompletedAtAfterFirstRun, afterSecondRun.FinalisationCompletedAt);
        }
    }


    [Fact]
    public async Task PerEmployeeIsolation_OneEmployeesFinalisationFailure_DoesNotBlockTheOtherEmployeeInTheSameBatch()
    {
        var companyId = Guid.NewGuid();
        Guid healthyEmployeeId, healthyProcessId, failingEmployeeId, failingProcessId, failingManagerId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

            failingManagerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Failing", "Manager");
            await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Failing", "Report", failingManagerId);

            healthyEmployeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Healthy", "Leaver");

            var healthyEmployee = await employeesDb.Employees.SingleAsync(e => e.Id == healthyEmployeeId);
            healthyEmployee.SetLeaving(DateTimeOffset.UtcNow);
            var failingEmployee = await employeesDb.Employees.SingleAsync(e => e.Id == failingManagerId);
            failingEmployee.SetLeaving(DateTimeOffset.UtcNow);
            failingEmployeeId = failingManagerId;

            var now = DateTimeOffset.UtcNow;
            var pastDueDate = new DateOnly(2020, 1, 1);

            var healthyProcess = CreateInProgressProcess(companyId, healthyEmployeeId, pastDueDate, now);
            healthyProcessId = healthyProcess.Id;
            var failingProcess = CreateInProgressProcess(companyId, failingEmployeeId, pastDueDate, now);
            failingProcessId = failingProcess.Id;

            employeesDb.EmployeeLeavingProcesses.AddRange(healthyProcess, failingProcess);
            await employeesDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var companyTimeZoneReader = scope.ServiceProvider.GetRequiredService<ICompanyTimeZoneReader>();
            var auditEventPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
            var integrationEventPublisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            var offboardingStatusReader = scope.ServiceProvider.GetRequiredService<IOffboardingStatusReader>();
            var leavingSettingsReader = scope.ServiceProvider.GetRequiredService<ICompanyLeavingSettingsReader>();
            var notificationWriter = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
            var timelineWriter = scope.ServiceProvider.GetRequiredService<IEmployeeTimelineWriter>();
            var realDirectReportsReader = scope.ServiceProvider.GetRequiredService<IDirectReportsReader>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProcessLeavingEmployeesJob>>();

            var throwingDirectReportsReader = new ThrowingForOneManagerDirectReportsReader(realDirectReportsReader, failingManagerId);

            var finalizer = new EmployeeDepartureFinalizer(
                employeesDb, auditEventPublisher, integrationEventPublisher, offboardingStatusReader,
                leavingSettingsReader, notificationWriter, timelineWriter, throwingDirectReportsReader);

            var job = new ProcessLeavingEmployeesJob(
                employeesDb, clock, companyTimeZoneReader, finalizer, logger);

            // Must not throw despite one employee's finalisation failing — the job's own per-employee
            // try/catch (ProcessDueLeaversAsync) isolates it.
            await job.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

            var healthyProcess = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == healthyProcessId);
            Assert.Equal(LeavingProcessStatus.Completed, healthyProcess.Status);
            Assert.NotNull(healthyProcess.FinalisationCompletedAt);
            var healthyEmployee = await employeesDb.Employees.SingleAsync(e => e.Id == healthyEmployeeId);
            Assert.Equal(EmploymentStatus.FormerEmployee, healthyEmployee.Status);

            // The failing employee's process must NOT have been finalised — the failure happened
            // before PersistTerminalStateAsync's SaveChangesAsync (the throwing read is the very
            // first thing CascadeManagerDepartureAsync does), so nothing was partially persisted.
            var failingProcess = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == failingProcessId);
            Assert.Equal(LeavingProcessStatus.InProgress, failingProcess.Status);
            Assert.Null(failingProcess.FinalisationCompletedAt);
        }
    }

    private sealed class ThrowingForOneManagerDirectReportsReader(
        IDirectReportsReader inner, Guid throwForManagerId) : IDirectReportsReader
    {
        public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(Guid companyId, Guid managerId, CancellationToken cancellationToken)
        {
            if (managerId == throwForManagerId)
                throw new InvalidOperationException("Simulated transient read failure for this manager's direct reports.");
            return inner.GetDirectReportIdsAsync(companyId, managerId, cancellationToken);
        }

        public Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(Guid companyId, Guid managerId, CancellationToken cancellationToken) =>
            inner.GetAllDescendantIdsAsync(companyId, managerId, cancellationToken);
    }
}
