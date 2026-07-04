using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.RemoveMyEmergencyContact;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class RemoveMyEmergencyContactHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Removes_The_Contact()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);

        var contact = EmergencyContact.Create(Guid.NewGuid(), employee.Id, companyId, "Jane Doe", "Spouse", "07700 900000", null, now);
        context.EmergencyContacts.Add(contact);
        await context.SaveChangesAsync();

        var handler = new RemoveMyEmergencyContactHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, contact.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await context.EmergencyContacts.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Contact_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new RemoveMyEmergencyContactHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Remove_Another_Employees_Contact()
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

        var handler = new RemoveMyEmergencyContactHandler(context);
        var result = await handler.HandleAsync(companyId, employee.Id, otherContact.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Single(await context.EmergencyContacts.ToListAsync());
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
