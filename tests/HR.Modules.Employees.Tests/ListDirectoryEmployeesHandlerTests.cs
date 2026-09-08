using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListDirectoryEmployees;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListDirectoryEmployeesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static Employee NewEmployee(
        Guid companyId, string first, string last, string email,
        Guid? employmentTypeId = null, Guid? departmentId = null, Guid? locationId = null, Guid? positionProfileId = null)
        => Employee.Create(
            Guid.NewGuid(), companyId, first, last, email, StartDate, hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            employmentTypeId ?? Guid.NewGuid(), departmentId ?? Guid.NewGuid(),
            locationId ?? Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(), Now);

    private static Employee Active(Employee e) { e.Activate(Now); return e; }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Suspended")]
    [InlineData("Leaving")]
    [InlineData("FormerEmployee")]
    public async Task HandleAsync_Excludes_NonActive_Employees(string statusName)
    {
        var status = Enum.Parse<EmploymentStatus>(statusName);
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = Active(NewEmployee(companyId, "Anna", "Active", "anna@example.com"));
        var other = NewEmployee(companyId, "Ned", "NonActive", "ned@example.com");
        other.SetStatusForTesting(status, Now);
        context.Employees.AddRange(active, other);
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(active.Id, result.Value.Items.Single().Id);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Other_Companies()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Employees.AddRange(
            Active(NewEmployee(companyId, "Alice", "Smith", "alice@example.com")),
            Active(NewEmployee(Guid.NewGuid(), "Carol", "Other", "carol@other.com")));
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Smith", result.Value.Items.Single().LastName);
    }

    [Theory]
    [InlineData("alice")]     // first name
    [InlineData("smith")]     // last name
    [InlineData("alice smith")] // full name
    [InlineData("ally")]      // preferred name
    [InlineData("acme")]      // work email
    public async Task HandleAsync_Search_Matches_Name_PreferredName_And_Email(string term)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var match = Active(NewEmployee(companyId, "Alice", "Smith", "alice@acme.com"));
        match.UpdatePersonalDetails("Ally", new DateOnly(1990, 1, 1), "British", "Prefer not to say", null, Now);
        var noMatch = Active(NewEmployee(companyId, "Bob", "Jones", "bob@globex.com"));
        context.Employees.AddRange(match, noMatch);
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, Search = term }, CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(match.Id, result.Value.Items.Single().Id);
    }

    [Fact]
    public async Task HandleAsync_Search_Matches_Department_And_Position()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, Guid.NewGuid(),
            "Software Developer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(profile);

        var byDept = Active(NewEmployee(companyId, "Dana", "Dept", "dana@example.com", departmentId: department.Id));
        var byPosition = Active(NewEmployee(companyId, "Pat", "Pos", "pat@example.com", positionProfileId: profile.Id));
        var neither = Active(NewEmployee(companyId, "Ned", "Nope", "ned@example.com"));
        context.Employees.AddRange(byDept, byPosition, neither);
        await context.SaveChangesAsync();

        var byDeptResult = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, Search = "engineer" }, CancellationToken.None);
        var byPositionResult = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, Search = "developer" }, CancellationToken.None);

        Assert.Equal(byDept.Id, Assert.Single(byDeptResult.Value!.Items).Id);
        Assert.Equal(byPosition.Id, Assert.Single(byPositionResult.Value!.Items).Id);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DepartmentId_And_LocationId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var match = Active(NewEmployee(companyId, "Match", "Both", "match@example.com",
            departmentId: departmentId, locationId: locationId));
        var wrongDept = Active(NewEmployee(companyId, "Wrong", "Dept", "wd@example.com", locationId: locationId));
        var wrongLoc = Active(NewEmployee(companyId, "Wrong", "Loc", "wl@example.com", departmentId: departmentId));
        context.Employees.AddRange(match, wrongDept, wrongLoc);
        await context.SaveChangesAsync();

        var byDept = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, DepartmentId = departmentId }, CancellationToken.None);
        var byBoth = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, DepartmentId = departmentId, LocationId = locationId },
            CancellationToken.None);

        Assert.Equal(2, byDept.Value!.TotalCount);
        Assert.Equal(match.Id, Assert.Single(byBoth.Value!.Items).Id);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_LastName_Then_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Employees.AddRange(
            Active(NewEmployee(companyId, "Bob", "Smith", "bob@example.com")),
            Active(NewEmployee(companyId, "Alice", "Smith", "alice@example.com")),
            Active(NewEmployee(companyId, "Carol", "Jones", "carol@example.com")));
        await context.SaveChangesAsync();

        var result = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        var items = result.Value!.Items;
        Assert.Equal("Jones", items[0].LastName);
        Assert.Equal("Alice", items[1].FirstName);
        Assert.Equal("Bob", items[2].FirstName);
    }

    [Fact]
    public async Task HandleAsync_Pages_Results_And_Computes_TotalPages()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            context.Employees.Add(Active(NewEmployee(companyId, "Employee", $"Z{i:00}", $"emp{i}@example.com")));
        await context.SaveChangesAsync();

        var page1 = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, PageNumber = 1, PageSize = 2 }, CancellationToken.None);
        var page2 = await Handler(context).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(3, page1.Value.TotalPages);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(2, page1.Value.PageSize);
        var page1Ids = page1.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(page2.Value!.Items, i => page1Ids.Contains(i.Id));
    }

    [Fact]
    public async Task HandleAsync_Resolves_Department_Location_Position_Names_And_PhotoUrl()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, Now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, Now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, Guid.NewGuid(),
            "Software Developer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(profile);

        var employee = Active(NewEmployee(companyId, "Alice", "Smith", "alice@example.com",
            departmentId: department.Id, locationId: location.Id, positionProfileId: profile.Id));
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var photoReader = new FakeProfilePhotoReader();
        photoReader.PhotoUrls[employee.Id] = "https://example.com/alice.jpg";

        var result = await new ListDirectoryEmployeesHandler(context, photoReader).HandleAsync(
            new ListDirectoryEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        var item = result.Value!.Items.Single();
        Assert.Equal("Engineering", item.DepartmentName);
        Assert.Equal(department.Id, item.DepartmentId);
        Assert.Equal("Head Office", item.LocationName);
        Assert.Equal(location.Id, item.LocationId);
        Assert.Equal("Software Developer", item.PositionTitle);
        Assert.Equal("https://example.com/alice.jpg", item.ProfilePhotoUrl);
    }

    private static ListDirectoryEmployeesHandler Handler(EmployeesDbContext context)
        => new(context, new FakeProfilePhotoReader());

    private static EmployeesDbContext BuildContext()
        => new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
