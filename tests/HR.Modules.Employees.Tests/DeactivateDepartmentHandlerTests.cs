using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivateDepartment;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivateDepartmentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Active_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = companyId, Id = dept.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.Departments.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Already_Inactive_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Legacy", null, FixedOffset);
        dept.Deactivate(FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = companyId, Id = dept.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Department_Has_Active_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), dept.Id, Guid.NewGuid(), Guid.NewGuid(), FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = companyId, Id = dept.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("Engineering", result.Error.Message);
        Assert.Contains("1 active employee", result.Error.Message);

        var saved = await context.Departments.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Department_When_Only_Terminated_Employees_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), dept.Id, Guid.NewGuid(), Guid.NewGuid(), FixedOffset);
        employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = companyId, Id = dept.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.Departments.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Wrong_Company()
    {
        await using var context = BuildContext();
        var dept = Department.Create(Guid.NewGuid(), Guid.NewGuid(), "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new DeactivateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateDepartmentRequest { CompanyId = Guid.NewGuid(), Id = dept.Id },
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
