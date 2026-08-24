using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.StartLeavingProcess;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class StartLeavingProcessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now, Guid? managerId = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        if (managerId.HasValue)
            employee.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), managerId.Value, now);

        return employee;
    }

    private static StartLeavingProcessRequest BuildRequest(
        Guid companyId, Guid employeeId, DateOnly? leavingDate = null, bool confirmBackdatedLeavingDate = false) =>
        new(
            companyId,
            employeeId,
            ResignationReceivedDate: new DateOnly(2026, 7, 1),
            LeavingDate: leavingDate ?? new DateOnly(2026, 8, 1),
            LastWorkingDay: (leavingDate ?? new DateOnly(2026, 8, 1)).AddDays(-1),
            LeavingReason.Resignation,
            confirmBackdatedLeavingDate);

    // Builds a real EmployeeDepartureFinalizer from the same Fakes passed to the handler so
    // assertions on auditPublisher/notificationWriter state after a confirmed-backdated
    // HandleAsync call cover both LeavingProcessStartedAuditEvent and
    // EmployeeDepartureFinalisedAuditEvent published through the same instance.
    private static StartLeavingProcessHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher? auditPublisher = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeOffboardingPlanCoordinator? offboardingPlanCoordinator = null,
        EffectiveNoticePeriod? effectiveNoticePeriod = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeCompanyLeavingSettingsReader? leavingSettingsReader = null,
        DateTime? fixedUtcNow = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null)
    {
        auditPublisher ??= new FakeAuditPublisher();
        notificationWriter ??= new FakeNotificationWriter();

        var departureFinalizer = new EmployeeDepartureFinalizer(
            context,
            auditPublisher,
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader ?? new FakeOffboardingStatusReader(new OffboardingStatusSummary("Completed")),
            leavingSettingsReader ?? new FakeCompanyLeavingSettingsReader(),
            notificationWriter,
            new FakeEmployeeTimelineWriter());

        return new(
            context,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            new FakeEffectiveNoticePeriodResolver(effectiveNoticePeriod),
            auditPublisher,
            new NoOpIntegrationEventPublisher(),
            notificationWriter,
            offboardingPlanCoordinator ?? new FakeOffboardingPlanCoordinator(),
            departureFinalizer);
    }

    [Fact]
    public async Task HandleAsync_Creates_LeavingProcess_With_Resolved_Effective_NoticePeriod()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var effectiveNoticePeriod = new EffectiveNoticePeriod(NoticePeriodUnit.Weeks, 6, NoticePeriodSource.PositionProfile);
        var handler = BuildHandler(context, effectiveNoticePeriod: effectiveNoticePeriod);
        var actorUserId = Guid.NewGuid();

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), actorUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(employee.Id, result.Value.EmployeeId);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.ResignationReceivedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.LeavingDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.Value.LastWorkingDay);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Value.NoticePeriodUnit);
        Assert.Equal(6, result.Value.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.PositionProfile.ToString(), result.Value.NoticeSource);
        Assert.Equal(LeavingReason.Resignation.ToString(), result.Value.LeavingReason);
        Assert.Equal(LeavingProcessStatus.InProgress.ToString(), result.Value.Status);
        Assert.Equal(now, result.Value.StartedAt);

        var saved = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(employee.Id, saved.EmployeeId);
        Assert.Equal(NoticePeriodUnit.Weeks, saved.NoticePeriodUnit);
        Assert.Equal(6, saved.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.PositionProfile, saved.NoticeSource);
        Assert.Equal(LeavingProcessStatus.InProgress, saved.Status);
        Assert.Equal(actorUserId, saved.StartedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Sets_Employee_Status_To_Leaving()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.Activate(now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Leaving, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Calls_OffboardingPlanCoordinator_StartAsync()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var offboardingPlanCoordinator = new FakeOffboardingPlanCoordinator();
        var handler = BuildHandler(context, offboardingPlanCoordinator: offboardingPlanCoordinator);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(offboardingPlanCoordinator.StartCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(employee.Id, call.EmployeeId);
        Assert.Equal(new DateOnly(2026, 7, 31), call.LastWorkingDay);
        Assert.Null(call.Notes);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeavingProcessStartedAuditEvent_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);
        var actorUserId = Guid.NewGuid();

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), actorUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<LeavingProcessStartedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employee.Id, auditEvent.EmployeeId);
        Assert.Equal(result.Value!.Id, auditEvent.LeavingProcessId);
        Assert.Equal(actorUserId, auditEvent.ActorEmployeeId);
        Assert.Equal(new DateOnly(2026, 7, 1), auditEvent.After.ResignationReceivedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), auditEvent.After.LeavingDate);
        Assert.Equal(new DateOnly(2026, 7, 31), auditEvent.After.LastWorkingDay);
        Assert.Equal(LeavingReason.Resignation, auditEvent.After.LeavingReason);
        Assert.Equal(LeavingProcessStatus.InProgress, auditEvent.After.Status);
    }

    [Fact]
    public async Task HandleAsync_Sends_Notifications_To_Manager_And_Employee_When_Manager_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = CreateEmployee(companyId, now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = CreateEmployee(companyId, now, managerId: manager.Id);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var handler = BuildHandler(context, notificationWriter: notificationWriter);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, notificationWriter.Written.Count);
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == manager.Id && n.Type == NotificationType.LeavingProcessStarted);
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == employee.Id && n.Type == NotificationType.LeavingProcessStarted);
    }

    [Fact]
    public async Task HandleAsync_Sends_Notification_To_Employee_Only_When_No_Manager_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var handler = BuildHandler(context, notificationWriter: notificationWriter);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(employee.Id, notification.EmployeeId);
        Assert.Equal(NotificationType.LeavingProcessStarted, notification.Type);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_InProgress_LeavingProcess_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);

        var existingLeavingProcess = EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employee.Id,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30),
            NoticePeriodUnit.Months, 1, NoticePeriodSource.CompanyDefault, LeavingReason.Resignation,
            Guid.NewGuid(), now);
        context.EmployeeLeavingProcesses.Add(existingLeavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
        Assert.Equal(1, await context.EmployeeLeavingProcesses.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_LeavingDate_Is_Backdated_And_Not_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, leavingDate: new DateOnly(2026, 6, 1), confirmBackdatedLeavingDate: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
        Assert.Equal(0, await context.EmployeeLeavingProcesses.CountAsync());

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Draft, savedEmployee.Status);
    }

    [Fact]
    public async Task HandleAsync_Finalizes_Employee_Departure_When_LeavingDate_Is_Backdated_And_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, leavingDate: new DateOnly(2026, 6, 1), confirmBackdatedLeavingDate: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeavingProcessStatus.Completed.ToString(), result.Value!.Status);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.FormerEmployee, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedProcess.Status);

        Assert.Equal(2, auditPublisher.Published.Count);
        Assert.Contains(auditPublisher.Published, e => e is LeavingProcessStartedAuditEvent);
        Assert.Contains(auditPublisher.Published, e => e is EmployeeDepartureFinalisedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Finalize_Employee_Departure_When_LeavingDate_Is_In_The_Future()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeavingProcessStatus.InProgress.ToString(), result.Value!.Status);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Leaving, savedEmployee.Status);

        var auditEvent = Assert.IsType<LeavingProcessStartedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.NotNull(auditEvent);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
