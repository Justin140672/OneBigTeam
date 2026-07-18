using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetOrganisationChart;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetOrganisationChartHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly Dob = new(1990, 1, 1);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        await using var context = BuildContext();
        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Employees_For_Requested_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var mine = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        mine.Activate(now);

        var other = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Bob", "Jones", "bob@other.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Male", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        other.Activate(now);

        context.Employees.AddRange(mine, other);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(mine.Id, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Employees_Of_All_Statuses_When_No_Status_Filter_Applied()
    {
        // Status is an optional filter, not a hardcoded restriction — with none supplied, every
        // employee regardless of status is returned (the Organisation Chart page itself defaults
        // its own Status dropdown to Active, but the handler stays a generic, flexible filter).
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var draft = Employee.Create(Guid.NewGuid(), companyId, "Dana", "Draft", "dana@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        var active = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Active", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        active.Activate(now);

        var terminated = Employee.Create(Guid.NewGuid(), companyId, "Tom", "Terminated", "tom@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Male", "EMP-0003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        terminated.Activate(now);
        terminated.Terminate(now);

        context.Employees.AddRange(draft, active, terminated);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_Active_Excludes_Draft_Employees()
    {
        await AssertStatusFilteredAsync(employee => { });
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_Active_Excludes_OnLeave_Employees()
    {
        await AssertStatusFilteredAsync(employee =>
        {
            employee.Activate(default);
            employee.SetOnLeave(default);
        });
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_Active_Excludes_Suspended_Employees()
    {
        await AssertStatusFilteredAsync(employee =>
        {
            employee.Activate(default);
            employee.Suspend(default);
        });
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_Active_Excludes_Terminated_Employees()
    {
        await AssertStatusFilteredAsync(employee =>
        {
            employee.Activate(default);
            employee.Terminate(default);
        });
    }

    // Transitions the given employee (starting Draft) via `transition`, then confirms an explicit
    // Status = Active request excludes them (they never reach Active themselves in these cases).
    private static async Task AssertStatusFilteredAsync(Action<Employee> transition)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        transition(employee);

        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId, Status = EmploymentStatus.Active },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DepartmentId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var engineering = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var sales = Department.Create(Guid.NewGuid(), companyId, "Sales", null, now);
        context.Departments.AddRange(engineering, sales);

        var engineer = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), engineering.Id, Guid.NewGuid(), Guid.NewGuid(), now);
        engineer.Activate(now);

        var salesperson = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Male", "EMP-0002", Guid.NewGuid(), sales.Id, Guid.NewGuid(), Guid.NewGuid(), now);
        salesperson.Activate(now);

        context.Employees.AddRange(engineer, salesperson);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId, DepartmentId = engineering.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(engineer.Id, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_LocationId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var london = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "London Office", null, now);
        var manchester = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Manchester Office", null, now);
        context.LocationTypes.Add(locationType);
        context.Locations.AddRange(london, manchester);

        var londonEmployee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), london.Id, Guid.NewGuid(), now);
        londonEmployee.Activate(now);

        var manchesterEmployee = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Male", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), manchester.Id, Guid.NewGuid(), now);
        manchesterEmployee.Activate(now);

        context.Employees.AddRange(londonEmployee, manchesterEmployee);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId, LocationId = london.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(londonEmployee.Id, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Department_Location_JobTitle_And_Includes_Manager_And_Photo()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "London Office", null, now);
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Senior Software Engineer", null,
            null, null, null, null, null, null, Guid.NewGuid(), now);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Mia", "Manager", "mia@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0000", Guid.NewGuid(), department.Id, location.Id, positionProfile.Id, now);
        manager.Activate(now);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), department.Id, location.Id, positionProfile.Id, now);
        employee.Activate(now);
        employee.Assign(department.Id, positionProfile.Id, location.Id, manager.Id, now);

        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(positionProfile);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var photoReader = new FakeProfilePhotoReader();
        photoReader.PhotoUrls[employee.Id] = "https://example.com/alice.jpg";

        var handler = new GetOrganisationChartHandler(context, photoReader);

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items, i => i.EmployeeId == employee.Id);

        Assert.Equal("Alice Smith", item.Name);
        Assert.Equal("EMP-0001", item.EmployeeNumber);
        Assert.Equal("Senior Software Engineer", item.JobTitle);
        Assert.Equal("Engineering", item.Department);
        Assert.Equal("London Office", item.Location);
        Assert.Equal(manager.Id, item.ManagerId);
        Assert.Equal("https://example.com/alice.jpg", item.ProfilePhotoUrl);
    }

    [Fact]
    public async Task HandleAsync_ProfilePhotoUrl_Is_Null_For_Employee_Without_Live_Photo()
    {
        // FakeProfilePhotoReader has no entry seeded for this employee — the reader's
        // "not found = absent" convention must surface as a null ProfilePhotoUrl, not an exception.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Activate(now);

        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.ProfilePhotoUrl);
    }

    [Fact]
    public async Task HandleAsync_ManagerId_Is_Null_For_Employee_With_No_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, Dob, "British", "Female", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.Activate(now);

        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new GetOrganisationChartHandler(context, new FakeProfilePhotoReader());

        var result = await handler.HandleAsync(
            new GetOrganisationChartRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.ManagerId);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
