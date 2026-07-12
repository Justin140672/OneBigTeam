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

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
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

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
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

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
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

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DepartmentName);
        Assert.Null(result.Value.LocationName);
        Assert.Null(result.Value.PositionTitle);
        Assert.Null(result.Value.ManagerFullName);
    }

    [Fact]
    public async Task HandleAsync_Returns_DepartmentName_LocationName_PositionTitle_And_ManagerFullName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        var position   = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, null, "Senior Developer", null, null, null, null, null, null, null, null, now);
        var manager    = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(position);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(department.Id, position.Id, location.Id, manager.Id, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering", result.Value!.DepartmentName);
        Assert.Equal("Head Office", result.Value.LocationName);
        Assert.Equal(location.Id, result.Value.LocationId);
        Assert.Equal("Senior Developer", result.Value.PositionTitle);
        Assert.Equal("Jane Manager", result.Value.ManagerFullName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zero_DirectReportsCount_When_No_Reports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.DirectReportsCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_DirectReportsCount_Excluding_Terminated_Reports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var reportOne = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        reportOne.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), manager.Id, now);

        var reportTwo = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        reportTwo.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), manager.Id, now);

        var terminatedReport = Employee.Create(Guid.NewGuid(), companyId, "Carl", "Leaver", "carl@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0004", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        terminatedReport.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), manager.Id, now);
        terminatedReport.Terminate(now);

        context.Employees.AddRange(reportOne, reportTwo, terminatedReport);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = manager.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.DirectReportsCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_ReportingChain_When_No_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.ReportingChain);
    }

    [Fact]
    public async Task HandleAsync_Returns_ReportingChain_Ordered_Root_First_Down_To_Immediate_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        var ceoProfile      = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, null, "CEO", null, null, null, null, null, null, null, null, now);
        var directorProfile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, null, "Director", null, null, null, null, null, null, null, null, now);
        var managerProfile  = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, null, "Manager", null, null, null, null, null, null, null, null, now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.AddRange(ceoProfile, directorProfile, managerProfile);
        await context.SaveChangesAsync();

        var ceo = Employee.Create(Guid.NewGuid(), companyId, "Carla", "Ceo", "carla@example.com", StartDate, hasSystemAccess: true, new DateOnly(1970, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ceoProfile.Id, now);
        context.Employees.Add(ceo);
        await context.SaveChangesAsync();

        var director = Employee.Create(Guid.NewGuid(), companyId, "Dan", "Director", "dan@example.com", StartDate, hasSystemAccess: true, new DateOnly(1980, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), directorProfile.Id, now);
        director.Assign(department.Id, directorProfile.Id, location.Id, ceo.Id, now);
        context.Employees.Add(director);
        await context.SaveChangesAsync();

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Mona", "Manager", "mona@example.com", StartDate, hasSystemAccess: true, new DateOnly(1985, 1, 1), "British", "Prefer not to say", "EMP-0003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), managerProfile.Id, now);
        manager.Assign(department.Id, managerProfile.Id, location.Id, director.Id, now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0004", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(department.Id, Guid.NewGuid(), location.Id, manager.Id, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.ReportingChain.Count);
        Assert.Equal("Carla Ceo", result.Value.ReportingChain[0].Name);
        Assert.Equal("CEO", result.Value.ReportingChain[0].JobTitle);
        Assert.Equal("Dan Director", result.Value.ReportingChain[1].Name);
        Assert.Equal("Mona Manager", result.Value.ReportingChain[2].Name);
    }

    [Fact]
    public async Task HandleAsync_ReportingChain_Stops_On_Circular_Manager_Reference()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employeeA = Employee.Create(Guid.NewGuid(), companyId, "Amy", "A", "amy@example.com", StartDate, hasSystemAccess: true, new DateOnly(1980, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var employeeB = Employee.Create(Guid.NewGuid(), companyId, "Ben", "B", "ben@example.com", StartDate, hasSystemAccess: true, new DateOnly(1980, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(employeeA, employeeB);
        await context.SaveChangesAsync();

        // A and B report to each other (data corruption / bad import) — must not infinite-loop.
        employeeA.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), employeeB.Id, now);
        employeeB.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), employeeA.Id, now);
        await context.SaveChangesAsync();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Assign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), employeeA.Id, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeHandler(context);
        var result = await handler.HandleAsync(
            new GetEmployeeRequest { CompanyId = companyId, Id = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ReportingChain.Count);
        Assert.Equal("Ben B", result.Value.ReportingChain[0].Name);
        Assert.Equal("Amy A", result.Value.ReportingChain[1].Name);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
