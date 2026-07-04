using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyContactDetails;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetMyContactDetailsHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Returns_Contact_Details_For_Own_Employee_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.UpdateContactDetails("alice.personal@example.com", "07700 900000", "01234 567890",
            "1 Test Street", null, "London", null, "SW1A 1AA", "United Kingdom", now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetMyContactDetailsHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice@example.com", result.Value!.WorkEmail);
        Assert.Equal("alice.personal@example.com", result.Value.PersonalEmail);
        Assert.Equal("SW1A 1AA", result.Value.PostCode);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        await using var context = BuildContext();
        var handler = new GetMyContactDetailsHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetMyContactDetailsHandler(context);
        var result = await handler.HandleAsync(Guid.NewGuid(), employee.Id, CancellationToken.None);

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
