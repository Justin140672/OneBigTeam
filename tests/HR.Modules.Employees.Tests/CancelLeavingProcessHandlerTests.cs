using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CancelLeavingProcess;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CancelLeavingProcessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.SetLeaving(now);
        return employee;
    }

    private static EmployeeLeavingProcess CreateLeavingProcess(Guid companyId, Guid employeeId, DateTimeOffset now) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

    private static CancelLeavingProcessRequest BuildRequest(Guid companyId, Guid employeeId) =>
        new(companyId, employeeId, CancellationReason: "Employee retracted resignation.");

    private static CancelLeavingProcessHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher? auditPublisher = null,
        FakeOffboardingStatusReader? offboardingStatusReader = null,
        FakeOffboardingPlanCoordinator? offboardingPlanCoordinator = null) =>
        new(
            context,
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            offboardingStatusReader ?? new FakeOffboardingStatusReader(),
            offboardingPlanCoordinator ?? new FakeOffboardingPlanCoordinator());

    [Fact]
    public async Task HandleAsync_Cancels_LeavingProcess_When_Offboarding_Not_Started()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, offboardingStatusReader: new FakeOffboardingStatusReader(null));

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeavingProcessStatus.Cancelled.ToString(), result.Value!.Status);
        Assert.False(result.Value.OffboardingTasksCancelled);

        var savedProcess = await context.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(LeavingProcessStatus.Cancelled, savedProcess.Status);
    }

    [Fact]
    public async Task HandleAsync_Reactivates_Employee_To_Active()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Active, savedEmployee.Status);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Call_OffboardingPlanCoordinator_When_Offboarding_Not_Started()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var handler = BuildHandler(
            context, offboardingStatusReader: new FakeOffboardingStatusReader(null), offboardingPlanCoordinator: coordinator);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(coordinator.CancelOutstandingTasksCalls);
    }

    [Fact]
    public async Task HandleAsync_Calls_OffboardingPlanCoordinator_When_Offboarding_Already_Started()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var handler = BuildHandler(
            context,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")),
            offboardingPlanCoordinator: coordinator);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.OffboardingTasksCancelled);
        var call = Assert.Single(coordinator.CancelOutstandingTasksCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(employee.Id, call.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeavingProcessCancelledAuditEvent_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(
            context, auditPublisher: auditPublisher,
            offboardingStatusReader: new FakeOffboardingStatusReader(new OffboardingStatusSummary("InProgress")));
        var actorUserId = Guid.NewGuid();

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), actorUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<LeavingProcessCancelledAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employee.Id, auditEvent.EmployeeId);
        Assert.Equal(leavingProcess.Id, auditEvent.LeavingProcessId);
        Assert.Equal(actorUserId, auditEvent.ActorEmployeeId);
        Assert.Equal("Employee retracted resignation.", auditEvent.CancellationReason);
        Assert.True(auditEvent.OffboardingTasksCancelled);
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
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, now.AddDays(-1));
        leavingProcess.Cancel("Already cancelled.", now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
