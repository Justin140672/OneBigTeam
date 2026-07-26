using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AssignManager;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AssignManagerHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Assigns_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(manager.Id, result.Value!.ManagerId);
        Assert.Equal("Jane Manager", result.Value.ManagerFullName);

        var saved = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(manager.Id, saved.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Removes_Manager_When_ManagerId_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), manager.Id, now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = null },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ManagerId);
        Assert.Null(result.Value.ManagerFullName);
    }

    [Fact]
    public async Task HandleAsync_Preserves_LocationId_When_Assigning_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(Guid.NewGuid(), Guid.NewGuid(), locationId, null, now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(manager.Id, result.Value!.ManagerId);

        var saved = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(locationId, saved.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), ManagerId = null },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = Guid.NewGuid(), Id = employee.Id, ManagerId = null },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = Guid.NewGuid() },
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

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        manager.SetStatusForTesting(EmploymentStatus.FormerEmployee, now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_For_Direct_Circular_Assignment()
    {
        // A → B, then try to assign A as manager of B
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var empA = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var empB = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        // B reports to A
        empB.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empA.Id, now);
        context.Employees.AddRange(empA, empB);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        // Try to assign B as manager of A — circular
        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = empA.Id, ManagerId = empB.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_For_Deep_Circular_Assignment()
    {
        // A → B → C, then try to assign A as manager of C
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var empA = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var empB = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var empC = Employee.Create(Guid.NewGuid(), companyId, "Carol", "White", "carol@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        // B reports to A; C reports to B
        empB.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empA.Id, now);
        empC.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empB.Id, now);
        context.Employees.AddRange(empA, empB, empC);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        // Try to assign C as manager of A — would create A→B→C→A cycle
        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = empA.Id, ManagerId = empC.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Valid_Reassignment_Within_Hierarchy()
    {
        // A → B → C, then reassign C to report to A directly (not circular)
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var empA = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var empB = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var empC = Employee.Create(Guid.NewGuid(), companyId, "Carol", "White", "carol@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        empB.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empA.Id, now);
        empC.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empB.Id, now);
        context.Employees.AddRange(empA, empB, empC);
        await context.SaveChangesAsync();

        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = empC.Id, ManagerId = empA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(empA.Id, result.Value!.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ManagerChanged_When_Manager_Actually_Changes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<HR.SharedKernel.EmployeeManagerChangedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(employee.Id, evt.EmployeeId);
        Assert.Null(evt.PreviousManagerId);
        Assert.Equal(manager.Id, evt.NewManagerId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_ManagerChanged_When_Manager_Is_Unchanged()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, manager.Id, now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new AssignManagerHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new AssignManagerRequest { CompanyId = companyId, Id = employee.Id, ManagerId = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(publisher.Published);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
