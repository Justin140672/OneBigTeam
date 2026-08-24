using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AmendLeavingProcess;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AmendLeavingProcessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);

    private static EmployeeLeavingProcess CreateLeavingProcess(Guid companyId, Guid employeeId, DateTimeOffset now) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

    private static AmendLeavingProcessRequest BuildRequest(
        Guid companyId, Guid employeeId, DateOnly? leavingDate = null, bool confirmBackdatedLeavingDate = false) =>
        new(
            companyId,
            employeeId,
            LeavingDate: leavingDate ?? new DateOnly(2026, 9, 1),
            LastWorkingDay: (leavingDate ?? new DateOnly(2026, 9, 1)).AddDays(-1),
            LeavingReason.MutualAgreement,
            confirmBackdatedLeavingDate);

    // Builds a real EmployeeDepartureFinalizer from the same Fakes passed to the handler so
    // assertions on auditPublisher state after a confirmed-backdated HandleAsync call cover both
    // LeavingProcessAmendedAuditEvent and EmployeeDepartureFinalisedAuditEvent published through
    // the same instance.
    private static AmendLeavingProcessHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher? auditPublisher = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeCompanyLeavingSettingsReader? leavingSettingsReader = null,
        FakeNotificationWriter? notificationWriter = null,
        DateTime? fixedUtcNow = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null)
    {
        auditPublisher ??= new FakeAuditPublisher();
        offboardingStatusReader ??= new FakeOffboardingStatusReader();

        var departureFinalizer = new EmployeeDepartureFinalizer(
            context,
            auditPublisher,
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader,
            leavingSettingsReader ?? new FakeCompanyLeavingSettingsReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            new FakeEmployeeTimelineWriter());

        return new(
            context,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            auditPublisher,
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader,
            departureFinalizer);
    }

    [Fact]
    public async Task HandleAsync_Amends_LeavingDate_LastWorkingDay_And_Reason()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value!.LeavingDate);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Value.LastWorkingDay);
        Assert.Equal(LeavingReason.MutualAgreement.ToString(), result.Value.LeavingReason);

        var saved = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 1), saved.LeavingDate);
        Assert.Equal(new DateOnly(2026, 8, 31), saved.LastWorkingDay);
        Assert.Equal(LeavingReason.MutualAgreement, saved.LeavingReason);
    }

    [Fact]
    public async Task HandleAsync_Leaves_NoticePeriod_Fields_Untouched()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Value!.NoticePeriodUnit);
        Assert.Equal(4, result.Value.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.Employee.ToString(), result.Value.NoticeSource);

        var saved = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(NoticePeriodUnit.Weeks, saved.NoticePeriodUnit);
        Assert.Equal(4, saved.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.Employee, saved.NoticeSource);
    }

    [Fact]
    public async Task HandleAsync_Returns_OffboardingAlreadyStarted_False_When_Reader_Returns_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, offboardingStatusReader: new FakeOffboardingStatusReader(null));

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.OffboardingAlreadyStarted);
    }

    [Fact]
    public async Task HandleAsync_Returns_OffboardingAlreadyStarted_True_When_Reader_Returns_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context, offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")));

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.OffboardingAlreadyStarted);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeavingProcessAmendedAuditEvent_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(
            context, auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")));
        var actorUserId = Guid.NewGuid();

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), actorUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<LeavingProcessAmendedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(leavingProcess.Id, auditEvent.LeavingProcessId);
        Assert.Equal(actorUserId, auditEvent.ActorEmployeeId);
        Assert.Equal(new DateOnly(2026, 8, 1), auditEvent.Before.LeavingDate);
        Assert.Equal(new DateOnly(2026, 9, 1), auditEvent.After.LeavingDate);
        Assert.Equal(new DateOnly(2026, 7, 31), auditEvent.Before.LastWorkingDay);
        Assert.Equal(new DateOnly(2026, 8, 31), auditEvent.After.LastWorkingDay);
        Assert.True(auditEvent.OffboardingAlreadyStarted);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_InProgress_LeavingProcess_Exists()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeavingProcess_Is_Not_InProgress()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        leavingProcess.Cancel("Retracted.", now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static Employee CreateEmployee(Guid id, Guid companyId, DateTimeOffset now)
    {
        var employee = Employee.Create(
            id, companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.SetLeaving(now);
        return employee;
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_LeavingDate_Is_Backdated_And_Not_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(employeeId, companyId, now);
        context.Employees.Add(employee);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leavingDate: new DateOnly(2026, 6, 1), confirmBackdatedLeavingDate: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 1), savedProcess.LeavingDate);
        Assert.Equal(LeavingProcessStatus.InProgress, savedProcess.Status);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Leaving, savedEmployee.Status);
    }

    [Fact]
    public async Task HandleAsync_Finalizes_Employee_Departure_When_LeavingDate_Is_Backdated_And_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(employeeId, companyId, now);
        context.Employees.Add(employee);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leavingDate: new DateOnly(2026, 6, 1), confirmBackdatedLeavingDate: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeavingProcessStatus.Completed.ToString(), result.Value!.Status);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.FormerEmployee, savedEmployee.Status);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Completed, savedProcess.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), savedProcess.LeavingDate);

        Assert.Equal(2, auditPublisher.Published.Count);
        Assert.Contains(auditPublisher.Published, e => e is LeavingProcessAmendedAuditEvent);
        Assert.Contains(auditPublisher.Published, e => e is EmployeeDepartureFinalisedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Finalize_Employee_Departure_When_LeavingDate_Is_In_The_Future()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(employeeId, companyId, now);
        context.Employees.Add(employee);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeavingProcessStatus.InProgress.ToString(), result.Value!.Status);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Leaving, savedEmployee.Status);

        var auditEvent = Assert.IsType<LeavingProcessAmendedAuditEvent>(Assert.Single(auditPublisher.Published));
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
