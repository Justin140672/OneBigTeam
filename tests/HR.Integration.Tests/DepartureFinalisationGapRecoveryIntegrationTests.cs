using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DepartureFinalisationGapRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinalisationGapRecoveryIntegrationTests(ApiWebApplicationFactory factory)
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

    private sealed class ThrowingManagerChangedHandler : IRequiredIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
    {
        public Task HandleAsync(EmployeeManagerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated Probation ManagerChangedHandler failure.");
    }

    private sealed class ThrowingEmployeeDepartureFinalisedHandler : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
    {
        public Task HandleAsync(EmployeeDepartureFinalisedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated Leave EmployeeDepartureFinalisedHandler failure.");
    }


    [Fact]
    public async Task Gap1_ManagerChangedHandlerFailure_LeavesEventUnpublished_ThenReconciliationDeliversItExactlyOnce()
    {
        var companyId = Guid.NewGuid();
        Guid managerId, replacementManagerId, reportId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

            managerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Gap1", "Manager");
            replacementManagerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Gap1", "Replacement");
            reportId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Gap1", "Report", managerId);

            var now = DateTimeOffset.UtcNow;

            var probationDb = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
            var probationRecord = ProbationRecord.Create(
                Guid.NewGuid(), companyId, reportId, managerId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), notes: null,
                today: new DateOnly(2026, 1, 1), now: now);
            probationDb.ProbationRecords.Add(probationRecord);
            await probationDb.SaveChangesAsync();

            var process = CreateInProgressProcess(companyId, managerId, new DateOnly(2026, 1, 1), now, replacementManagerId);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);
            await employeesDb.SaveChangesAsync();
        }

        await using (var throwingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>>();
                services.AddScoped<IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>, ThrowingManagerChangedHandler>();
            });
        }))
        {
            using var scope = throwingFactory.Services.CreateScope();
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == managerId);
            var process = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            var finalizer = scope.ServiceProvider.GetRequiredService<IEmployeeDepartureFinalizer>();

            // Must not throw despite the required consumer failing — PublishAndConfirmAsync itself
            // never propagates a handler's exception to its caller.
            await finalizer.FinalizeAsync(employee, process, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var pendingEvent = await employeesDb.PendingManagerChangedEvents
                .SingleAsync(e => e.CompanyId == companyId && e.LeavingProcessId == processId && e.ReportEmployeeId == reportId);
            Assert.Null(pendingEvent.PublishedAt);

            // Probation's own required consumer never actually ran, so its side effect must not have
            // happened either.
            var probationDb = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
            var record = await probationDb.ProbationRecords.SingleAsync(r => r.EmployeeId == reportId);
            Assert.Equal(managerId, record.ManagerEmployeeId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcilePendingManagerChangedEventsJob>();
            await reconcileJob.ExecuteAsync();
        }

        DateTimeOffset? publishedAtAfterFirstRun;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var pendingEvent = await employeesDb.PendingManagerChangedEvents
                .SingleAsync(e => e.CompanyId == companyId && e.LeavingProcessId == processId && e.ReportEmployeeId == reportId);
            Assert.NotNull(pendingEvent.PublishedAt);
            publishedAtAfterFirstRun = pendingEvent.PublishedAt;

            var probationDb = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
            var record = await probationDb.ProbationRecords.SingleAsync(r => r.EmployeeId == reportId);
            Assert.Equal(replacementManagerId, record.ManagerEmployeeId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcilePendingManagerChangedEventsJob>();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var rows = await employeesDb.PendingManagerChangedEvents
                .Where(e => e.CompanyId == companyId && e.LeavingProcessId == processId && e.ReportEmployeeId == reportId)
                .ToListAsync();
            Assert.Single(rows);
            Assert.Equal(publishedAtAfterFirstRun, rows[0].PublishedAt);
        }
    }

    [Fact]
    public async Task Gap1_UnrelatedEmployeesDeparture_StillCompletesNormally_WhileAnotherIsFailing()
    {
        var companyId = Guid.NewGuid();
        Guid managerId, reportId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

            managerId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Healthy", "Manager");
            reportId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Healthy", "Report", managerId);

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

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var pendingEvent = await employeesDb.PendingManagerChangedEvents
                .SingleAsync(e => e.CompanyId == companyId && e.LeavingProcessId == processId && e.ReportEmployeeId == reportId);
            Assert.NotNull(pendingEvent.PublishedAt);
        }
    }


    [Fact]
    public async Task Gap2_EmployeeDepartureFinalisedHandlerFailure_LeavesNoRequestRow_ThenReconciliationCreatesAndProcessesIt()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Gap2", "Departing");

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

        await using (var throwingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>>();
                services.AddScoped<IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>, ThrowingEmployeeDepartureFinalisedHandler>();
            });
        }))
        {
            using var scope = throwingFactory.Services.CreateScope();
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == employeeId);
            var process = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            var finalizer = scope.ServiceProvider.GetRequiredService<IEmployeeDepartureFinalizer>();

            await finalizer.FinalizeAsync(employee, process, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var process = await employeesDb.EmployeeLeavingProcesses.SingleAsync(p => p.Id == processId);
            Assert.NotNull(process.FinalisationCompletedAt);

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var rows = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
                .ToListAsync();
            Assert.Empty(rows);

            var assignmentStillActive = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignmentStillActive.IsActive);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcileMissingLeaveDeactivationsJob>();
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
            var request = await leaveDb.LeavePolicyDeactivationsOnDeparture.SingleAsync(d => d.Id == deactivationId);
            Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, request.Status);

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.False(assignment.IsActive);
        }

        // Idempotency: running the reconciliation job again must not attempt (or succeed at) creating
        // a duplicate request — the row already exists (now Processed), so it is simply skipped.
        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcileMissingLeaveDeactivationsJob>();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var rows = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
                .ToListAsync();
            Assert.Single(rows);
        }
    }

    [Fact]
    public async Task Gap2_UnrelatedEmployeesDeparture_StillDeactivatesLeavePolicyNormally_WhileAnotherIsFailing()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId, processId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "HealthyLeave", "Departing");

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

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .SingleAsync(d => d.CompanyId == companyId && d.EmployeeId == employeeId);
            Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
        }
    }


    private async Task<Guid> SeedHistoricallyFinalisedDepartureAsync(
        EmployeesDbContext employeesDb, Guid companyId, Guid employeeId, DateTimeOffset finalisationCompletedAt)
    {
        var now = DateTimeOffset.UtcNow;
        var process = CreateInProgressProcess(companyId, employeeId, DateOnly.FromDateTime(finalisationCompletedAt.UtcDateTime), now);
        process.Complete(now);
        process.MarkFinalisationCompleted(finalisationCompletedAt);
        employeesDb.EmployeeLeavingProcesses.Add(process);
        await employeesDb.SaveChangesAsync();
        return process.Id;
    }

    [Fact]
    public async Task Gap2Historical_DepartureFinalisedOver30DaysAgo_IsMissedByTheDailyJob_ButRepairedByTheHistoricalJob()
    {
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Historical", "Departed");

            var employee = await employeesDb.Employees.SingleAsync(e => e.Id == employeeId);
            employee.SetFormerEmployee(DateTimeOffset.UtcNow);

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var now = DateTimeOffset.UtcNow;
            var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, isDefault: true, now: now);
            leaveDb.LeavePolicies.Add(policy);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2018, 1, 1), now);
            assignmentId = assignment.Id;
            leaveDb.EmployeeLeavePolicyAssignments.Add(assignment);
            await leaveDb.SaveChangesAsync();

            await SeedHistoricallyFinalisedDepartureAsync(
                employeesDb, companyId, employeeId, DateTimeOffset.UtcNow.AddDays(-400));
        }

        // The regular daily job must NOT find/fix this — proving the 30-day/entire-history split is
        // real, not just documentation.
        using (var scope = _factory.Services.CreateScope())
        {
            var reconcileJob = scope.ServiceProvider.GetRequiredService<ReconcileMissingLeaveDeactivationsJob>();
            await reconcileJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var rows = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
                .ToListAsync();
            Assert.Empty(rows);

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignment.IsActive);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var existingProgress = await leaveDb.HistoricalLeaveDeactivationRepairProgress
                .Where(p => p.Id == HistoricalLeaveDeactivationRepairProgress.SingletonId)
                .ToListAsync();
            leaveDb.HistoricalLeaveDeactivationRepairProgress.RemoveRange(existingProgress);
            await leaveDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var historicalJob = scope.ServiceProvider.GetRequiredService<ReconcileHistoricalLeaveDeactivationsJob>();
            await historicalJob.ExecuteAsync();
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
    public async Task Gap2Historical_RehiredEmployee_HistoricalDepartureIsNotUsedToDeactivateTheirCurrentAssignment()
    {
        // Requirement 6: rehire safety. The employee is CURRENT (non-former) again by the time the
        // historical sweep reaches their old, stale departure record — their present-day, legitimate
        // assignment must not be touched.
        var companyId = Guid.NewGuid();
        Guid employeeId, assignmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);
            employeeId = await SeedEmployeeAsync(employeesDb, companyId, referenceData, "Rehired", "Employee");

            await SeedHistoricallyFinalisedDepartureAsync(
                employeesDb, companyId, employeeId, DateTimeOffset.UtcNow.AddDays(-400));


            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var now = DateTimeOffset.UtcNow;
            var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, isDefault: true, now: now);
            leaveDb.LeavePolicies.Add(policy);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2026, 1, 1), now);
            assignmentId = assignment.Id;
            leaveDb.EmployeeLeavePolicyAssignments.Add(assignment);
            await leaveDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var historicalJob = scope.ServiceProvider.GetRequiredService<ReconcileHistoricalLeaveDeactivationsJob>();
            await historicalJob.ExecuteAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var rows = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
                .ToListAsync();
            Assert.Empty(rows);

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignment.IsActive);
        }
    }
}
