using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Returns_Employee_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employee.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Alice", result.Value.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
        Assert.Equal("alice@example.com", result.Value.WorkEmail);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(EmploymentStatus.Draft, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);

        // Request uses a different companyId — should not find the employee
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = Guid.NewGuid(), Id = employee.Id },
            CancellationToken.None);

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
