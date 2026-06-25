using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Persistence;
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

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
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

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
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

    [Fact]
    public async Task HandleAsync_Returns_HasSystemAccess_In_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: false, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Display_Names_When_No_Related_Entities()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DepartmentName);
        Assert.Null(result.Value.PositionTitle);
        Assert.Null(result.Value.ManagerFullName);
    }

    [Fact]
    public async Task HandleAsync_Returns_DepartmentName_PositionTitle_And_ManagerFullName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var position   = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, "Senior Developer", null, false, null, now);
        var manager    = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(position);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.Assign(department.Id, position.Id, manager.Id, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering", result.Value!.DepartmentName);
        Assert.Equal("Senior Developer", result.Value.PositionTitle);
        Assert.Equal("Jane Manager", result.Value.ManagerFullName);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
