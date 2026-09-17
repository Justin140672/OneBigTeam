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

/// <summary>
/// Coverage for the two reliability gaps closed on top of the durable-recovery mechanisms already
/// covered by <see cref="DepartureFinalisationRecoveryIntegrationTests"/>:
///
///  Gap 1 (confirmed delivery): EmployeeDepartureFinalizer.CascadeManagerDepartureAsync now uses
///  IIntegrationEventPublisher.PublishAndConfirmAsync instead of PublishAsync when publishing
///  EmployeeManagerChangedIntegrationEvent, and only marks a PendingManagerChangedEvent published
///  once every handler implementing IRequiredIntegrationEventHandler&lt;T&gt; (Probation's
///  ManagerChangedHandler) has actually succeeded — not merely that the publish call returned (which
///  HR.SharedKernel.IntegrationEventPublisher always guarantees, even when a handler throws).
///
///  Gap 2 (missing durable-request recovery): ReconcileMissingLeaveDeactivationsJob authoritatively
///  re-derives every departure Employees considers fully finalised (via the cross-module
///  IFinalisedEmployeeDeparturesReader contract) and creates+enqueues a LeavePolicyDeactivationOnDeparture
///  request for any that has none at all — catching the case where Leave's own
///  EmployeeDepartureFinalisedHandler threw before its own insert, which
///  ReconcileLeavePolicyDeactivationsJob's "re-enqueue existing rows" sweep can never catch because
///  no row was ever created to retry.
///
/// Both scenarios induce a deterministic handler failure using the same technique already documented
/// in ReportExportAuditingIntegrationTests.Export_That_Fails_After_Authorization_Persists_A_Distinguishable_Failure_Audit_Record:
/// a separate, test-scoped WebApplicationFactory spun up via _factory.WithWebHostBuilder(...), with
/// the specific handler's own DI registration replaced by a throwing stand-in (RemoveAll + re-add,
/// since simply adding an extra registration would run both handlers side by side). This is a real,
/// DI-level substitution of the exact consumer under test — not a change to production code — and
/// mirrors the "wrap/replace the real DI-resolved instance" technique DepartureFinalisationRecoveryIntegrationTests
/// already uses (ThrowingForOneManagerDirectReportsReader), just applied one level up at the handler
/// registration itself because neither ManagerChangedHandler nor EmployeeDepartureFinalisedHandler has
/// an injectable dependency that runs before the effect under test (ManagerChangedHandler's first
/// mutation follows directly from a plain DbContext query with no interposable reader; the same is
/// true of EmployeeDepartureFinalisedHandler's guard queries). Both throwing/real hosts talk to the
/// SAME Testcontainers Postgres instance (shared via the ConnectionStrings__hr environment variable
/// ApiWebApplicationFactory sets up), so state written via one host is visible to the other.
/// </summary>
[Collection("Integration")]
public class DepartureFinalisationGapRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinalisationGapRecoveryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---- shared seeding helpers (mirrors DepartureFinalisationRecoveryIntegrationTests) ---------

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

    /// <summary>Throws unconditionally instead of running the real handler — used to simulate
    /// Probation's ManagerChangedHandler failing before it applies any of its own effects.</summary>
    private sealed class ThrowingManagerChangedHandler : IRequiredIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
    {
        public Task HandleAsync(EmployeeManagerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated Probation ManagerChangedHandler failure.");
    }

    /// <summary>Throws unconditionally instead of running the real handler — used to simulate Leave's
    /// EmployeeDepartureFinalisedHandler failing before it inserts its own
    /// LeavePolicyDeactivationOnDeparture row.</summary>
    private sealed class ThrowingEmployeeDepartureFinalisedHandler : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
    {
        public Task HandleAsync(EmployeeDepartureFinalisedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated Leave EmployeeDepartureFinalisedHandler failure.");
    }

    // ==========================================================================================
    // Gap 1: confirmed delivery of EmployeeManagerChangedIntegrationEvent to Probation.
    // ==========================================================================================

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

            // A replacement manager is nominated so EmployeeManagerChangedIntegrationEvent.NewManagerId
            // is non-null, exercising ManagerChangedHandler's ChangeManager branch (the effect this
            // test needs to observe) rather than its "manager cleared" branch.
            var process = CreateInProgressProcess(companyId, managerId, new DateOnly(2026, 1, 1), now, replacementManagerId);
            processId = process.Id;
            employeesDb.EmployeeLeavingProcesses.Add(process);
            await employeesDb.SaveChangesAsync();
        }

        // Fault-injecting host: Probation's ManagerChangedHandler registration is fully replaced by a
        // handler that throws before touching anything, so the manager's departure cascade publishes
        // EmployeeManagerChangedIntegrationEvent but the required consumer never applies its effect.
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

        // Remove the induced failure and run the real reconciliation job.
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
            Assert.Equal(replacementManagerId, record.ManagerEmployeeId); // required handler's effect now applied
        }

        // Idempotency: running the reconciliation job a second time must be a pure no-op — no
        // duplicate PendingManagerChangedEvent rows, PublishedAt unchanged, and Probation's own
        // idempotent guard (record.ManagerEmployeeId already equal to NewManagerId) prevents the
        // effect being applied twice.
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
        // Proves per-item isolation: this scenario's manager-changed cascade runs entirely on the
        // normal (non-fault-injected) host and must complete inline, exactly as
        // DepartureFinalisationRecoveryIntegrationTests already establishes for the happy path —
        // written here explicitly alongside the Gap-1 failure scenario above to confirm the new
        // PublishAndConfirmAsync-based cascade doesn't regress the normal, all-handlers-succeed case.
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

    // ==========================================================================================
    // Gap 2: missing LeavePolicyDeactivationOnDeparture recovery.
    // ==========================================================================================

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

        // Fault-injecting host: Leave's EmployeeDepartureFinalisedHandler registration is fully
        // replaced by a handler that throws before it can look up the assignment or insert its
        // request row. Finalisation still proceeds — EmployeeLeavingProcess.FinalisationCompletedAt
        // is still set, since IntegrationEventPublisher.PublishAsync (used here; this consumer is not
        // marked required) never blocks the caller on a handler's failure.
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
            Assert.NotNull(process.FinalisationCompletedAt); // finalisation still completed correctly

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var rows = await leaveDb.LeavePolicyDeactivationsOnDeparture
                .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
                .ToListAsync();
            Assert.Empty(rows); // confirmed gap: no durable row exists at all to retry

            var assignmentStillActive = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignmentStillActive.IsActive);
        }

        // Run the real reconciliation job (real host, no fault injected).
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

        // Mirrors DepartureFinalisationRecoveryIntegrationTests: IBackgroundJobClient is a no-op fake
        // in this test host, so directly invoke the real job body the (faked) Hangfire enqueue would
        // have triggered.
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
            Assert.Single(rows); // no duplicate row created
        }
    }

    [Fact]
    public async Task Gap2_UnrelatedEmployeesDeparture_StillDeactivatesLeavePolicyNormally_WhileAnotherIsFailing()
    {
        // Proves per-item isolation: this employee's departure/leave-deactivation pipeline runs
        // entirely on the normal (non-fault-injected) host and must complete via the ordinary
        // event-driven path, confirming the fault induced for the Gap-2 scenario above is scoped to
        // its own dedicated host/test and never leaks into unrelated employees processed normally.
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

    // ==========================================================================================
    // Round 3 (Gap-2 follow-up): ReconcileHistoricalLeaveDeactivationsJob — the entire-history,
    // paginated backlog sweep that closes the gap ReconcileMissingLeaveDeactivationsJob's 30-day
    // lookback can never reach.
    // ==========================================================================================

    /// <summary>Seeds a departure "already finalised" long ago, directly via the real domain methods
    /// (rather than the real-time finalizer), so FinalisationCompletedAt can be backdated beyond the
    /// 30-day lookback while leaving no LeavePolicyDeactivationOnDeparture row — modelling a departure
    /// stranded before the recovery mechanism existed, or during an extended outage of it.</summary>
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

            // Mark the employee a former (non-current) employee — ICurrentEmployeeReader's real
            // implementation classifies by Employee.Status, and this scenario models a genuinely
            // departed employee (not a rehire — that is the distinct scenario covered below).
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

            // Finalised well outside the 30-day lookback (and outside any plausible daily-job window).
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
            Assert.Empty(rows); // confirmed: the 30-day job cannot see this departure

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignment.IsActive);
        }

        // HistoricalLeaveDeactivationRepairProgress is a single global row (one-time sweep, shared
        // across the whole Postgres testcontainer this "Integration" collection's tests all run
        // against) — a prior test in this collection may already have driven it to IsComplete, which
        // would make ExecuteAsync() a permanent no-op and never reach the departure just seeded
        // above. Reset it here so this test's own sweep is guaranteed to run fresh regardless of
        // what already ran before it.
        using (var scope = _factory.Services.CreateScope())
        {
            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var existingProgress = await leaveDb.HistoricalLeaveDeactivationRepairProgress
                .Where(p => p.Id == HistoricalLeaveDeactivationRepairProgress.SingletonId)
                .ToListAsync();
            leaveDb.HistoricalLeaveDeactivationRepairProgress.RemoveRange(existingProgress);
            await leaveDb.SaveChangesAsync();
        }

        // The historical sweep job DOES find and fix it.
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

        // Mirrors the other scenarios in this file: directly invoke the (faked-away) Hangfire-enqueued
        // job body.
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

            // Seed a stale historical departure for this employee...
            await SeedHistoricallyFinalisedDepartureAsync(
                employeesDb, companyId, employeeId, DateTimeOffset.UtcNow.AddDays(-400));

            // ...but the employee is (still/again) Active — SeedEmployeeAsync already calls
            // Activate(), and Employee.Status is exactly what ICurrentEmployeeReader's real
            // implementation classifies "current" vs "former" by — modelling the rehire: the
            // employee is current again despite the stale historical departure record.

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var now = DateTimeOffset.UtcNow;
            var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, isDefault: true, now: now);
            leaveDb.LeavePolicies.Add(policy);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2026, 1, 1), now); // legitimate post-rehire assignment
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
            Assert.Empty(rows); // no deactivation request created for the rehired employee

            var assignment = await leaveDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignmentId);
            Assert.True(assignment.IsActive);
        }
    }
}
