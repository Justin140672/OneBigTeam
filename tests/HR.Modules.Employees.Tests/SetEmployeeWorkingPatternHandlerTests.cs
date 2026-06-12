using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SetEmployeeWorkingPattern;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SetEmployeeWorkingPatternHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 6);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_Sets_Working_Pattern_Override()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var updateTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new SetEmployeeWorkingPatternHandler(context, new FakeClock(updateTime));
        var result = await handler.HandleAsync(
            new SetEmployeeWorkingPatternRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
                HoursPerDayOverride = 6m
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, result.Value!.WorkingDaysOverride);
        Assert.Equal(6m, result.Value.HoursPerDayOverride);
        Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), result.Value.UpdatedAt);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, saved.WorkingDaysOverride);
        Assert.Equal(6m, saved.HoursPerDayOverride);
    }

    [Fact]
    public async Task HandleAsync_Clears_Override_When_Nulls_Passed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.SetWorkingPattern(WorkingDays.Monday | WorkingDays.Friday, 7.5m, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new SetEmployeeWorkingPatternHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            new SetEmployeeWorkingPatternRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                WorkingDaysOverride = null,
                HoursPerDayOverride = null
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.WorkingDaysOverride);
        Assert.Null(result.Value.HoursPerDayOverride);

        var saved = await context.Employees.SingleAsync();
        Assert.Null(saved.WorkingDaysOverride);
        Assert.Null(saved.HoursPerDayOverride);
    }

    [Fact]
    public async Task HandleAsync_Can_Set_Days_Only_Without_Hours()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new SetEmployeeWorkingPatternHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            new SetEmployeeWorkingPatternRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Wednesday | WorkingDays.Friday,
                HoursPerDayOverride = null
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Wednesday | WorkingDays.Friday, result.Value!.WorkingDaysOverride);
        Assert.Null(result.Value.HoursPerDayOverride);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new SetEmployeeWorkingPatternHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetEmployeeWorkingPatternRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new SetEmployeeWorkingPatternHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            new SetEmployeeWorkingPatternRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = employee.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
