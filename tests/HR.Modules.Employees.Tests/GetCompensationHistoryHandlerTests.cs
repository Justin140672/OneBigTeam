using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetCompensationHistory;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetCompensationHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetCompensationHistoryHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Compensation_Records_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetCompensationHistoryHandler(context);

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Records_Ordered_By_EffectiveFrom_Descending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, Now);
        context.Employees.Add(employee);

        var oldest = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", 37.5m, 1m, "Starting salary", Now);
        var middle = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", 37.5m, 1m, null, Now);
        var newest = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", 37.5m, 1m, null, Now);
        context.Compensations.AddRange(middle, oldest, newest);
        await context.SaveChangesAsync();

        var handler = new GetCompensationHistoryHandler(context);

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Value.Items.Select(i => i.Id));
        Assert.Equal("Starting salary", result.Value.Items.Last().Notes);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Include_Records_From_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, Now);
        var otherEmployee = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", new DateOnly(2024, 1, 1), true, Now);
        context.Employees.AddRange(employee, otherEmployee);

        var mine = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, Now);
        var theirs = Compensation.Create(Guid.NewGuid(), companyId, otherEmployee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 60000m, "GBP", null, null, null, Now);
        context.Compensations.AddRange(mine, theirs);
        await context.SaveChangesAsync();

        var handler = new GetCompensationHistoryHandler(context);

        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(mine.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Include_Records_From_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, Now);
        context.Employees.Add(employee);

        var compensation = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, Now);
        context.Compensations.Add(compensation);
        await context.SaveChangesAsync();

        var handler = new GetCompensationHistoryHandler(context);

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
