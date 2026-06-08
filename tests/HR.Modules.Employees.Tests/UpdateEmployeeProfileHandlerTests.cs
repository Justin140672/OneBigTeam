using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateEmployeeProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Updates_Employee_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var updateTime = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(updateTime));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alicia",
                LastName = "Jones",
                WorkEmail = "alicia.jones@example.com",
                PersonalEmail = "alicia@gmail.com",
                StartDate = new DateOnly(2026, 8, 1)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alicia", result.Value!.FirstName);
        Assert.Equal("Jones", result.Value.LastName);
        Assert.Equal("alicia.jones@example.com", result.Value.WorkEmail);
        Assert.Equal("alicia@gmail.com", result.Value.PersonalEmail);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.StartDate);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal("Alicia", saved.FirstName);
        Assert.Equal("alicia.jones@example.com", saved.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Normalises_WorkEmail_To_Lowercase()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "Alice.SMITH@EXAMPLE.COM",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice.smith@example.com", result.Value!.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_WorkEmail_Already_Taken_By_Another_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var emp1 = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, now);
        var emp2 = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, now);
        context.Employees.AddRange(emp1, emp2);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = emp1.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "bob@example.com",  // taken by emp2
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Employee_To_Keep_Own_WorkEmail()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alicia",
                LastName = "Smith",
                WorkEmail = "alice@example.com",  // same email, same employee
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alicia", result.Value!.FirstName);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
