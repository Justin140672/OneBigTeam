using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateEmployeeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_Draft_Status()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Alice", result.Value.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
        Assert.Equal("alice.smith@example.com", result.Value.WorkEmail);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(EmploymentStatus.Draft, result.Value.Status);
        Assert.Null(result.Value.DepartmentId);
        Assert.Null(result.Value.PositionProfileId);
        Assert.Null(result.Value.ManagerId);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Normalises_WorkEmail_To_Lowercase()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Bob",
                LastName = "Jones",
                WorkEmail = "Bob.Jones@EXAMPLE.COM",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("bob.jones@example.com", result.Value!.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_Department_And_PositionProfile_And_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var positionProfile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, "Developer", null, false, now);
        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane.manager@example.com", StartDate, hasSystemAccess: true, now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(positionProfile);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                PositionProfileId = positionProfile.Id,
                ManagerId = manager.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(department.Id, result.Value!.DepartmentId);
        Assert.Equal(positionProfile.Id, result.Value.PositionProfileId);
        Assert.Equal(manager.Id, result.Value.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_WorkEmail_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyId, "Existing", "User", "alice.smith@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_WorkEmail_In_Different_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyB,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = Guid.NewGuid(),
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
    public async Task HandleAsync_Returns_NotFound_When_Department_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), Guid.NewGuid(), "Engineering", null, now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = department.Id,
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
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
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
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Developer", null, false, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
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
    public async Task HandleAsync_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                ManagerId = Guid.NewGuid(),
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
    public async Task HandleAsync_Returns_NotFound_When_Manager_Is_Terminated()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        manager.Terminate(now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                ManagerId = manager.Id,
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
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_True_By_Default()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_False_When_Specified()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HasSystemAccess = false
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasSystemAccess);

        var saved = await context.Employees.SingleAsync();
        Assert.False(saved.HasSystemAccess);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
