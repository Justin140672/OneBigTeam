using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyEmergencyContacts;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetMyEmergencyContactsHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Returns_Contacts_Ordered_By_CreatedAt()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);

        var first = EmergencyContact.Create(Guid.NewGuid(), employee.Id, companyId, "Jane Doe", "Spouse", "07700 900000", null, now);
        var second = EmergencyContact.Create(Guid.NewGuid(), employee.Id, companyId, "Bob Smith", "Parent", "01234 567890", "bob@example.com", now.AddMinutes(1));
        context.EmergencyContacts.AddRange(second, first);
        await context.SaveChangesAsync();

        var handler = new GetMyEmergencyContactsHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Contacts.Count);
        Assert.Equal("Jane Doe", result.Value.Contacts[0].Name);
        Assert.Equal("Bob Smith", result.Value.Contacts[1].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Contacts_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetMyEmergencyContactsHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Contacts);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        await using var context = BuildContext();
        var handler = new GetMyEmergencyContactsHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Another_Employees_Contacts()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        var otherEmployee = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(employee, otherEmployee);

        var otherContact = EmergencyContact.Create(Guid.NewGuid(), otherEmployee.Id, companyId, "Someone", "Friend", "07700 900000", null, now);
        context.EmergencyContacts.Add(otherContact);
        await context.SaveChangesAsync();

        var handler = new GetMyEmergencyContactsHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Contacts);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
