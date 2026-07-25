using System.Reflection;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetLeavingProcess;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetLeavingProcessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);

    private static EmployeeLeavingProcess CreateLeavingProcess(
        Guid companyId, Guid employeeId, DateTimeOffset now, LeavingProcessStatus status = LeavingProcessStatus.InProgress)
    {
        var leavingProcess = EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

        if (status != LeavingProcessStatus.InProgress)
        {
            // EmployeeLeavingProcess has no domain mutator for Status yet (that arrives with the
            // "Cancel Leaving Process" slice) — reflection mirrors EmployeeTestExtensions'
            // SetStatusForTesting pattern used elsewhere in this project for the same reason.
            typeof(EmployeeLeavingProcess).GetProperty(nameof(EmployeeLeavingProcess.Status), BindingFlags.Public | BindingFlags.Instance)!
                .SetValue(leavingProcess, status);
        }

        return leavingProcess;
    }

    [Fact]
    public async Task HandleAsync_Returns_LeavingProcess_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(companyId, employeeId, now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = new GetLeavingProcessHandler(context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(leavingProcess.Id, result.Value!.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.ResignationReceivedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.LeavingDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.Value.LastWorkingDay);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Value.NoticePeriodUnit);
        Assert.Equal(4, result.Value.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.Employee.ToString(), result.Value.NoticeSource);
        Assert.Equal(LeavingReason.Resignation.ToString(), result.Value.LeavingReason);
        Assert.Equal(LeavingProcessStatus.InProgress.ToString(), result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_None_Exists()
    {
        await using var context = BuildContext();
        var handler = new GetLeavingProcessHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeavingProcess_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leavingProcess = CreateLeavingProcess(Guid.NewGuid(), employeeId, now);
        context.EmployeeLeavingProcesses.Add(leavingProcess);
        await context.SaveChangesAsync();

        var handler = new GetLeavingProcessHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_LeavingProcess_When_Multiple_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var earlier = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var later = earlier.AddDays(30);

        // An earlier, cancelled leaving process for the same employee — GetLeavingProcess should
        // surface the most recent one regardless of status.
        var older = CreateLeavingProcess(companyId, employeeId, earlier, LeavingProcessStatus.Cancelled);
        var newer = CreateLeavingProcess(companyId, employeeId, later);
        context.EmployeeLeavingProcesses.AddRange(older, newer);
        await context.SaveChangesAsync();

        var handler = new GetLeavingProcessHandler(context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newer.Id, result.Value!.Id);
        Assert.Equal(LeavingProcessStatus.InProgress.ToString(), result.Value.Status);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
