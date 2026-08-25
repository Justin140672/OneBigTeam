using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// OFF-03: ActiveLeavingProcessReader is the Employees-side implementation of the cross-module port
// (IActiveLeavingProcessReader) Offboarding's reconciliation job depends on to find every employee
// with an InProgress leaving process but no active offboarding plan.
public class ActiveLeavingProcessReaderTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.SetLeaving(now);
        return employee;
    }

    private static EmployeeLeavingProcess CreateLeavingProcess(
        Guid companyId, Guid employeeId, DateOnly lastWorkingDay, DateTimeOffset now) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), lastWorkingDay,
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

    [Fact]
    public async Task GetInProgressLeavingProcessesAsync_Returns_InProgress_Leaving_Process()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var lastWorkingDay = new DateOnly(2026, 7, 31);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, lastWorkingDay, now.AddDays(-1));
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var reader = new ActiveLeavingProcessReader(context);

        var result = await reader.GetInProgressLeavingProcessesAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal(employee.Id, item.EmployeeId);
        Assert.Equal(lastWorkingDay, item.LastWorkingDay);
    }

    [Fact]
    public async Task GetInProgressLeavingProcessesAsync_Excludes_Cancelled_Leaving_Process()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, new DateOnly(2026, 7, 31), now.AddDays(-1));
        leavingProcess.Cancel("Retracted resignation.", now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var reader = new ActiveLeavingProcessReader(context);

        var result = await reader.GetInProgressLeavingProcessesAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetInProgressLeavingProcessesAsync_Excludes_Completed_Leaving_Process()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        var leavingProcess = CreateLeavingProcess(companyId, employee.Id, new DateOnly(2026, 7, 31), now.AddDays(-1));
        leavingProcess.Complete(now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var reader = new ActiveLeavingProcessReader(context);

        var result = await reader.GetInProgressLeavingProcessesAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetInProgressLeavingProcessesAsync_Returns_Empty_When_No_Leaving_Processes_Exist()
    {
        await using var context = BuildContext();

        var reader = new ActiveLeavingProcessReader(context);

        var result = await reader.GetInProgressLeavingProcessesAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}
