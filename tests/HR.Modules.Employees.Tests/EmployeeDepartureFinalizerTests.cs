using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Unit tests for EmployeeDepartureFinalizer in isolation, exercised directly rather than through
// ProcessLeavingEmployeesJob or the Start/AmendLeavingProcess handlers that also call it.
public class EmployeeDepartureFinalizerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

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

    private static EmployeeDepartureFinalizer BuildFinalizer(
        EmployeesDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeCompanyLeavingSettingsReader? leavingSettingsReader = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeEmployeeTimelineWriter? timelineWriter = null,
        CapturingIntegrationEventPublisher? integrationEventPublisher = null) =>
        new(
            dbContext,
            auditPublisher ?? new FakeAuditPublisher(),
            integrationEventPublisher ?? new CapturingIntegrationEventPublisher(),
            offboardingStatusReader ?? new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            leavingSettingsReader ?? new FakeCompanyLeavingSettingsReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            timelineWriter ?? new FakeEmployeeTimelineWriter());

    [Fact]
    public async Task FinalizeAsync_Writes_EmploymentEnded_Timeline_Entry_With_Correct_Fields()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var timelineWriter = new FakeEmployeeTimelineWriter();
        var finalizer = BuildFinalizer(context, timelineWriter: timelineWriter);

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employee.Id, entry.EmployeeId);
        Assert.Equal(EmployeeTimelineEventType.EmploymentEnded, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Equal(process.Id, entry.SourceRecordId);
        Assert.Equal("Employees", entry.SourceModule);
    }

    [Fact]
    public async Task FinalizeAsync_Sets_Employee_FormerEmployee_And_Completes_Process()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.FormerEmployee, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedProcess.Status);
    }

    [Fact]
    public async Task FinalizeAsync_Leaves_SystemAccess_Unchanged_When_AutoDisable_Setting_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now, hasSystemAccess: true);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(
            context, leavingSettingsReader: new FakeCompanyLeavingSettingsReader(autoDisableAccessOnLeavingDate: false));

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.True(savedEmployee.HasSystemAccess);
    }

    [Fact]
    public async Task FinalizeAsync_Disables_SystemAccess_And_Reports_AccessDisabled_When_AutoDisable_Setting_Is_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now, hasSystemAccess: true);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var finalizer = BuildFinalizer(
            context,
            auditPublisher: auditPublisher,
            leavingSettingsReader: new FakeCompanyLeavingSettingsReader(autoDisableAccessOnLeavingDate: true));

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.False(savedEmployee.HasSystemAccess);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.AccessDisabled);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData(null)]
    public async Task FinalizeAsync_Treats_Missing_Or_NonCompleted_OffboardingStatus_As_Incomplete(string? status)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var finalizer = BuildFinalizer(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(status is null ? null : new OffboardingStatusSummary(status)));

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(auditEvent.OffboardingIncomplete);
    }

    [Fact]
    public async Task FinalizeAsync_Reports_Offboarding_Complete_When_Status_Is_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var finalizer = BuildFinalizer(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")));

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.False(auditEvent.OffboardingIncomplete);
    }

    [Fact]
    public async Task FinalizeAsync_Notifies_Manager_When_Offboarding_Incomplete_And_Manager_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = CreateManager(companyId, Now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateLeavingEmployee(companyId, Now, managerId: manager.Id);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var finalizer = BuildFinalizer(
            context,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            notificationWriter: notificationWriter);

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(manager.Id, notification.EmployeeId);
        Assert.Equal(NotificationType.IncompleteOffboardingAtDeparture, notification.Type);
        Assert.Equal(NotificationPriority.High, notification.Priority);
        Assert.Equal(employee.Id, notification.SourceEntityId);
    }

    [Fact]
    public async Task FinalizeAsync_Sends_No_Notification_When_Offboarding_Incomplete_But_No_Manager_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var finalizer = BuildFinalizer(
            context,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            notificationWriter: notificationWriter);

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task FinalizeAsync_Sends_No_Notification_When_Offboarding_Complete_Even_With_Manager_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = CreateManager(companyId, Now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateLeavingEmployee(companyId, Now, managerId: manager.Id);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var finalizer = BuildFinalizer(
            context,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            notificationWriter: notificationWriter);

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task FinalizeAsync_Always_Publishes_EmployeeDepartureFinalisedAuditEvent_With_Correct_Values()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateLeavingEmployee(companyId, Now);
        context.Employees.Add(employee);
        var process = CreateLeavingProcess(companyId, employee.Id, DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        context.EmployeeLeavingProcesses.Add(process);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var finalizer = BuildFinalizer(
            context,
            auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            leavingSettingsReader: new FakeCompanyLeavingSettingsReader(autoDisableAccessOnLeavingDate: true));

        await finalizer.FinalizeAsync(employee, process, Now, CancellationToken.None);

        var auditEvent = Assert.IsType<EmployeeDepartureFinalisedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employee.Id, auditEvent.EmployeeId);
        Assert.Equal(process.Id, auditEvent.LeavingProcessId);
        Assert.Equal(Now, auditEvent.OccurredAt);
        Assert.True(auditEvent.AccessDisabled);
        Assert.True(auditEvent.OffboardingIncomplete);
    }
}
