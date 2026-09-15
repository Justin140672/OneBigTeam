using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

public class ProcessLeavingEmployeesJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // Creates an employee already in the Leaving status (the only status this job selects on).
    private static Employee CreateLeavingEmployee(
        Guid companyId, DateTimeOffset now, bool hasSystemAccess = true, Guid? managerId = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        if (managerId.HasValue)
            employee.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), managerId.Value, now);

        employee.SetLeaving(now);
        return employee;
    }

    // Creates a manager (an ordinary active employee, not themselves leaving) purely to act as
    // the target of a notification.
    private static Employee CreateManager(Guid companyId, DateTimeOffset now)
    {
        var manager = Employee.Create(
            Guid.NewGuid(), companyId, "Mary", "Manager", "mary@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1985, 1, 1), "British", "Prefer not to say", "EMP-0000",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        manager.Activate(now);
        return manager;
    }

    private static EmployeeLeavingProcess CreateLeavingProcess(
        Guid companyId, Guid employeeId, DateOnly leavingDate, DateTimeOffset now) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            leavingDate.AddMonths(-1), leavingDate, leavingDate.AddDays(-1),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

    private static ProcessLeavingEmployeesJob BuildJob(
        EmployeesDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeCompanyLeavingSettingsReader? leavingSettingsReader = null,
        FakeNotificationWriter? notificationWriter = null,
        DateTime? fixedUtcNow = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null)
    {
        var departureFinalizer = new EmployeeDepartureFinalizer(
            dbContext,
            auditPublisher ?? new FakeAuditPublisher(),
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader ?? new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            leavingSettingsReader ?? new FakeCompanyLeavingSettingsReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            new FakeEmployeeTimelineWriter(),
            new FakeDirectReportsReader());

        return new(
            dbContext,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            departureFinalizer,
            NullLogger<ProcessLeavingEmployeesJob>.Instance);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task ExecuteAsync_Transitions_Employee_And_Process_When_LeavingDate_Has_Passed_Or_Is_Today(
        int leavingDateOffsetDays)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(leavingDateOffsetDays), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var job = BuildJob(context);

        await job.ExecuteAsync();

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.FormerEmployee, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedProcess.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_SystemAccess_Unchanged_When_AutoDisable_Setting_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now, hasSystemAccess: true);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var job = BuildJob(context, leavingSettingsReader: new FakeCompanyLeavingSettingsReader(autoDisableAccessOnLeavingDate: false));

        await job.ExecuteAsync();

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.True(savedEmployee.HasSystemAccess);
    }

    [Fact]
    public async Task ExecuteAsync_Disables_SystemAccess_When_AutoDisable_Setting_Is_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now, hasSystemAccess: true);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            leavingSettingsReader: new FakeCompanyLeavingSettingsReader(autoDisableAccessOnLeavingDate: true));

        await job.ExecuteAsync();

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.False(savedEmployee.HasSystemAccess);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.AccessDisabled);
    }

    [Fact]
    public async Task ExecuteAsync_Notifies_Manager_And_Publishes_OffboardingIncomplete_Audit_When_Offboarding_Not_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = CreateManager(companyId, Now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateLeavingEmployee(companyId, Now, managerId: manager.Id);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            notificationWriter: notificationWriter);

        await job.ExecuteAsync();

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(manager.Id, notification.EmployeeId);
        Assert.Equal(NotificationType.IncompleteOffboardingAtDeparture, notification.Type);
        Assert.Equal(NotificationPriority.High, notification.Priority);
        Assert.Equal(employee.Id, notification.SourceEntityId);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.OffboardingIncomplete);
        Assert.Equal(process.Id, auditEvent.LeavingProcessId);
    }

    [Fact]
    public async Task ExecuteAsync_Treats_Null_OffboardingStatus_As_Incomplete()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(summary: null));

        await job.ExecuteAsync();

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.OffboardingIncomplete);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_When_Offboarding_Incomplete_But_No_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now); // no manager assigned
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            notificationWriter: notificationWriter);

        await job.ExecuteAsync();

        Assert.Empty(notificationWriter.Written);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.OffboardingIncomplete);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_And_Reports_Offboarding_Complete_When_Status_Is_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = CreateManager(companyId, Now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateLeavingEmployee(companyId, Now, managerId: manager.Id);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            notificationWriter: notificationWriter);

        await job.ExecuteAsync();

        Assert.Empty(notificationWriter.Written);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.False(auditEvent.OffboardingIncomplete);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_Employee_Untouched_When_LeavingDate_Is_In_The_Future()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher, notificationWriter: notificationWriter);

        await job.ExecuteAsync();

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Leaving, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.InProgress, savedProcess.Status);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Employee_Without_Throwing_When_No_InProgress_LeavingProcess_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // Inconsistent state: status Leaving but no matching InProgress leaving process.
        var inconsistentEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(inconsistentEmployee);

        // A normal, correctly-due employee in the same run to prove it is unaffected.
        var okEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(okEmployee);
        var okProcess = CreateLeavingProcess(companyId, okEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(okProcess);

        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var savedInconsistent = await context.Employees.SingleAsync(e => e.Id == inconsistentEmployee.Id);
        Assert.Equal(EmploymentStatus.Leaving, savedInconsistent.Status);

        var savedOk = await context.Employees.SingleAsync(e => e.Id == okEmployee.Id);
        Assert.Equal(EmploymentStatus.FormerEmployee, savedOk.Status);

        var savedOkProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedOkProcess.Status);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(okEmployee.Id, auditEvent.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Company_Local_Day_Not_UTC_Day_When_Determining_LeavingDate_Is_Due()
    {
        // 2026-07-25T23:30:00Z is still 2026-07-25 in UTC, but already 2026-07-26 00:30 in
        // Europe/London (BST, UTC+1). A leaving date of 2026-07-26 must be treated as "today" (due)
        // once the company's local timezone is applied, even though the UTC day is still the 25th.
        var fixedUtcNow = new DateTime(2026, 7, 25, 23, 30, 0, DateTimeKind.Utc);
        var localNow = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, localNow);
        context.Employees.Add(employee);

        var process = CreateLeavingProcess(companyId, employee.Id, new DateOnly(2026, 7, 26), localNow);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var job = BuildJob(
            context,
            fixedUtcNow: fixedUtcNow,
            companyTimeZoneReader: new FakeCompanyTimeZoneReader("Europe/London"));

        await job.ExecuteAsync();

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.FormerEmployee, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedProcess.Status);
    }

    // -- Stranded-departure reconciliation scan ----------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Reconciles_A_Stranded_Departure_That_ProcessDueLeavers_Would_Never_Pick_Up()
    {
        // A process left Completed with FinalisationCompletedAt still null (as if an earlier
        // FinalizeAsync attempt persisted the terminal state but crashed before the downstream
        // steps), whose employee is no longer Status == Leaving — ProcessDueLeaversAsync's query
        // would never select it, so only the reconciliation scan can pick it up.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = CreateManager(companyId, Now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateLeavingEmployee(companyId, Now, managerId: manager.Id);
        var process = CreateLeavingProcess(companyId, employee.Id, Today.AddDays(-1), Now);

        employee.SetFormerEmployee(Now);
        process.Complete(Now);
        Assert.Null(process.FinalisationCompletedAt);

        context.Employees.Add(employee);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var notificationWriter = new FakeNotificationWriter();
        var job = BuildJob(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            notificationWriter: notificationWriter);

        await job.ExecuteAsync();

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.NotNull(savedProcess.FinalisationCompletedAt);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(employee.Id, auditEvent.EmployeeId);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(manager.Id, notification.EmployeeId);
    }

    // -- Per-employee failure isolation (reliability fix) ------------------------------------

    private static EmployeeDepartureFinalizer BuildRealFinalizer(
        EmployeesDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeCompanyLeavingSettingsReader? leavingSettingsReader = null,
        FakeNotificationWriter? notificationWriter = null) =>
        new(
            dbContext,
            auditPublisher ?? new FakeAuditPublisher(),
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader ?? new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            leavingSettingsReader ?? new FakeCompanyLeavingSettingsReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            new FakeEmployeeTimelineWriter(),
            new FakeDirectReportsReader());

    [Fact]
    public async Task ExecuteAsync_A_Throwing_Due_Leaver_Does_Not_Stop_A_Later_Healthy_Due_Leaver_In_The_Same_Run()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var throwingEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(throwingEmployee);
        var throwingProcess = CreateLeavingProcess(companyId, throwingEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(throwingProcess);

        var healthyEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(healthyEmployee);
        var healthyProcess = CreateLeavingProcess(companyId, healthyEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(healthyProcess);

        await context.SaveChangesAsync();

        var finalizer = new SelectivelyThrowingDepartureFinalizer(BuildRealFinalizer(context), throwingEmployee.Id);
        var job = new ProcessLeavingEmployeesJob(
            context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), finalizer,
            NullLogger<ProcessLeavingEmployeesJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);
        Assert.Contains(throwingEmployee.Id, finalizer.InvokedFor);
        Assert.Contains(healthyEmployee.Id, finalizer.InvokedFor);

        var savedThrowing = await context.Employees.SingleAsync(e => e.Id == throwingEmployee.Id);
        Assert.Equal(EmploymentStatus.Leaving, savedThrowing.Status); // untouched by the failed attempt

        var savedHealthy = await context.Employees.SingleAsync(e => e.Id == healthyEmployee.Id);
        Assert.Equal(EmploymentStatus.FormerEmployee, savedHealthy.Status);
    }

    [Fact]
    public async Task ExecuteAsync_A_Throwing_Due_Leaver_Does_Not_Stop_The_Stranded_Recovery_Scan_From_Running_Afterward()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var throwingEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(throwingEmployee);
        var throwingProcess = CreateLeavingProcess(companyId, throwingEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(throwingProcess);

        // A stranded departure that only the reconciliation scan (not ProcessDueLeaversAsync) can pick up.
        var strandedEmployee = CreateLeavingEmployee(companyId, Now);
        var strandedProcess = CreateLeavingProcess(companyId, strandedEmployee.Id, Today.AddDays(-1), Now);
        strandedEmployee.SetFormerEmployee(Now);
        strandedProcess.Complete(Now);
        context.Employees.Add(strandedEmployee);
        context.EmployeeLeavingProcesses.Add(strandedProcess);

        await context.SaveChangesAsync();

        var finalizer = new SelectivelyThrowingDepartureFinalizer(BuildRealFinalizer(context), throwingEmployee.Id);
        var job = new ProcessLeavingEmployeesJob(
            context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), finalizer,
            NullLogger<ProcessLeavingEmployeesJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var savedStrandedProcess = await context.EmployeeLeavingProcesses.SingleAsync(p => p.Id == strandedProcess.Id);
        Assert.NotNull(savedStrandedProcess.FinalisationCompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Logs_Error_With_Employee_Company_And_Process_Ids_When_Due_Leaver_Finalisation_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var throwingEmployee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(throwingEmployee);
        var throwingProcess = CreateLeavingProcess(companyId, throwingEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(throwingProcess);
        await context.SaveChangesAsync();

        var finalizer = new SelectivelyThrowingDepartureFinalizer(BuildRealFinalizer(context), throwingEmployee.Id);
        var logger = new ListLogger<ProcessLeavingEmployeesJob>();
        var job = new ProcessLeavingEmployeesJob(
            context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), finalizer, logger);

        await job.ExecuteAsync();

        Assert.Contains(logger.Messages, m =>
            m.Contains(throwingEmployee.Id.ToString()) &&
            m.Contains(companyId.ToString()) &&
            m.Contains(throwingProcess.Id.ToString()));
    }

    [Fact]
    public async Task ExecuteAsync_A_Throwing_Stranded_Reconciliation_Does_Not_Stop_A_Later_Stranded_Record_In_The_Same_Run()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var throwingEmployee = CreateLeavingEmployee(companyId, Now);
        var throwingProcess = CreateLeavingProcess(companyId, throwingEmployee.Id, Today.AddDays(-1), Now);
        throwingEmployee.SetFormerEmployee(Now);
        throwingProcess.Complete(Now);
        context.Employees.Add(throwingEmployee);
        context.EmployeeLeavingProcesses.Add(throwingProcess);

        var healthyEmployee = CreateLeavingEmployee(companyId, Now);
        var healthyProcess = CreateLeavingProcess(companyId, healthyEmployee.Id, Today.AddDays(-1), Now);
        healthyEmployee.SetFormerEmployee(Now);
        healthyProcess.Complete(Now);
        context.Employees.Add(healthyEmployee);
        context.EmployeeLeavingProcesses.Add(healthyProcess);

        await context.SaveChangesAsync();

        var finalizer = new SelectivelyThrowingDepartureFinalizer(BuildRealFinalizer(context), throwingEmployee.Id);
        var job = new ProcessLeavingEmployeesJob(
            context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), finalizer,
            NullLogger<ProcessLeavingEmployeesJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var savedThrowingProcess = await context.EmployeeLeavingProcesses.SingleAsync(p => p.Id == throwingProcess.Id);
        Assert.Null(savedThrowingProcess.FinalisationCompletedAt);

        var savedHealthyProcess = await context.EmployeeLeavingProcesses.SingleAsync(p => p.Id == healthyProcess.Id);
        Assert.NotNull(savedHealthyProcess.FinalisationCompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Stranded_Process_Without_Throwing_When_Employee_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // A stranded leaving process referencing an employee that no longer exists in the DB.
        var orphanEmployeeId = Guid.NewGuid();
        var process = CreateLeavingProcess(companyId, orphanEmployeeId, Today.AddDays(-1), Now);
        process.Complete(Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var job = BuildJob(context);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Null(savedProcess.FinalisationCompletedAt);
    }
}
