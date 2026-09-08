using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetDirectoryEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetDirectoryEmployeeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static Employee NewEmployee(
        Guid companyId, string first, string last, string email,
        Guid? departmentId = null, Guid? locationId = null, Guid? positionProfileId = null)
        => Employee.Create(
            Guid.NewGuid(), companyId, first, last, email, StartDate, hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            Guid.NewGuid(), departmentId ?? Guid.NewGuid(),
            locationId ?? Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(), Now);

    [Fact]
    public async Task HandleAsync_Returns_Active_Employee_With_Resolved_Names_Manager_And_Photo()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, Now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, Now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, Guid.NewGuid(),
            "Software Developer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var manager = NewEmployee(companyId, "Jane", "Manager", "jane@example.com");
        manager.Activate(Now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(profile);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = NewEmployee(companyId, "Alice", "Smith", "alice@example.com",
            departmentId: department.Id, locationId: location.Id, positionProfileId: profile.Id);
        employee.Activate(Now);
        employee.Assign(department.Id, profile.Id, location.Id, manager.Id, Now);
        employee.UpdateContactDetails(null, "01234 567890", null, null, null, null, null, null, null, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var photoReader = new FakeProfilePhotoReader();
        photoReader.PhotoUrls[employee.Id] = "https://example.com/alice.jpg";

        var result = await new GetDirectoryEmployeeHandler(context, photoReader).HandleAsync(
            new GetDirectoryEmployeeRequest { CompanyId = companyId, Id = employee.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(employee.Id, value.Id);
        Assert.Equal("Alice", value.FirstName);
        Assert.Equal("Engineering", value.DepartmentName);
        Assert.Equal("Head Office", value.LocationName);
        Assert.Equal("Software Developer", value.PositionTitle);
        Assert.Equal("01234 567890", value.WorkPhone);
        Assert.Equal(StartDate, value.StartDate);
        Assert.Equal(manager.Id, value.ManagerId);
        Assert.Equal("Jane Manager", value.ManagerFullName);
        Assert.Equal("https://example.com/alice.jpg", value.ProfilePhotoUrl);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Suspended")]
    [InlineData("Leaving")]
    [InlineData("FormerEmployee")]
    public async Task HandleAsync_Returns_NotFound_For_NonActive_Employee(string statusName)
    {
        var status = Enum.Parse<EmploymentStatus>(statusName);
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = NewEmployee(companyId, "Sam", "Suspended", "sam@example.com");
        employee.SetStatusForTesting(status, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new GetDirectoryEmployeeRequest { CompanyId = companyId, Id = employee.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Employee_In_Another_Company()
    {
        await using var context = BuildContext();
        var employee = NewEmployee(Guid.NewGuid(), "Cross", "Company", "cross@example.com");
        employee.Activate(Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new GetDirectoryEmployeeRequest { CompanyId = Guid.NewGuid(), Id = employee.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Missing_Id()
    {
        await using var context = BuildContext();

        var result = await Handler(context).HandleAsync(
            new GetDirectoryEmployeeRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static GetDirectoryEmployeeHandler Handler(EmployeesDbContext context)
        => new(context, new FakeProfilePhotoReader());

    private static EmployeesDbContext BuildContext()
        => new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
