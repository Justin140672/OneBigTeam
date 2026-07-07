using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyEmployee;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetMyEmployeeHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Returns_Employee_Summary_With_PositionProfile_Title()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Senior Engineer", null, null, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.Assign(null, profile.Id, null, null, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetMyEmployeeHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alice", result.Value!.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
        Assert.Equal("Senior Engineer", result.Value.JobTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_JobTitle_When_No_PositionProfile_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetMyEmployeeHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.JobTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        await using var context = BuildContext();
        var handler = new GetMyEmployeeHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

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
