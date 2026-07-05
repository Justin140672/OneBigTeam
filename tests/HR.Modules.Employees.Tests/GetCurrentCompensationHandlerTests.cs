using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetCurrentCompensation;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetCurrentCompensationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Compensation_Records_Exist()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1), true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Latest_Record_Effective_Today_Or_Earlier()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, now);
        context.Employees.Add(employee);

        var older = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", 37.5m, 1m, null, now);
        var current = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", 37.5m, 1m, "Annual review", now);
        var future = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 50000m, "GBP", null, null, null, now);
        context.Compensations.AddRange(older, current, future);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(current.Id, result.Value!.Id);
        Assert.Equal(45000m, result.Value.Salary);
        Assert.Equal("Annual", result.Value.SalaryType);
        Assert.Equal("Annual review", result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Only_Future_Dated_Records_Exist()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, now);
        context.Employees.Add(employee);

        var future = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 50000m, "GBP", null, null, null, now);
        context.Compensations.Add(future);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Closed_Record_And_Returns_Open_Successor()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, now);
        context.Employees.Add(employee);

        var closed = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, now);
        closed.Close(new DateOnly(2025, 12, 31), now);
        var open = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.AddRange(closed, open);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(open.Id, result.Value!.Id);
        Assert.Null(result.Value.EffectiveTo);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Only_Closed_Record_Predates_Today()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var closed = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2020, 1, 1), SalaryType.Annual, 30000m, "GBP", null, null, null, now);
        closed.Close(new DateOnly(2021, 1, 1), now);
        context.Compensations.Add(closed);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Record_From_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, now);
        context.Employees.Add(employee);

        var compensation = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.Add(compensation);
        await context.SaveChangesAsync();

        var handler = new GetCurrentCompensationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(otherCompanyId, employee.Id, CancellationToken.None);

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
