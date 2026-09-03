using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SearchEmployeeDirectory;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SearchEmployeeDirectoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc));
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static SearchEmployeeDirectoryRequest Request(
        Guid companyId,
        string? term = null,
        bool includeLeavers = false,
        int limit = 20) =>
        new(companyId, term, includeLeavers, limit);

    private static Employee NewEmployee(
        Guid companyId,
        string firstName,
        string lastName,
        string workEmail = "person@example.com",
        string employeeNumber = "EMP-0001",
        DateOnly? startDate = null) =>
        Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, workEmail,
            startDate ?? StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", employeeNumber,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    [Fact]
    public async Task Matches_By_FirstName_CaseInsensitive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Alice", "Smith", "alice@example.com"),
            NewEmployee(companyId, "Bob", "Jones", "bob@example.com"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "ALICE"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Alice", item.FirstName);
    }

    [Fact]
    public async Task Matches_By_LastName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Alice", "Smith"),
            NewEmployee(companyId, "Bob", "Jones"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "jones"), CancellationToken.None);

        Assert.Equal("Bob", Assert.Single(result.Value!.Items).FirstName);
    }

    [Fact]
    public async Task Matches_By_Full_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "David", "Park"),
            NewEmployee(companyId, "Bob", "Jones"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "david park"), CancellationToken.None);

        Assert.Equal("David", Assert.Single(result.Value!.Items).FirstName);
    }

    [Fact]
    public async Task Matches_By_WorkEmail()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Alice", "Smith", "alice@acme.example"),
            NewEmployee(companyId, "Bob", "Jones", "bob@globex.example"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "acme"), CancellationToken.None);

        Assert.Equal("Alice", Assert.Single(result.Value!.Items).FirstName);
    }

    [Fact]
    public async Task Matches_By_EmployeeNumber()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Alice", "Smith", employeeNumber: "EMP-0042"),
            NewEmployee(companyId, "Bob", "Jones", employeeNumber: "EMP-0099"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "0042"), CancellationToken.None);

        Assert.Equal("Alice", Assert.Single(result.Value!.Items).FirstName);
    }

    [Fact]
    public async Task Term_Is_Trimmed_Before_Matching()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(NewEmployee(companyId, "Alice", "Smith"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "   alice   "), CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task Excludes_Employees_From_Other_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyA, "Alice", "Smith"),
            NewEmployee(companyB, "Alice", "Smithson"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyA, "smith"), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Smith", item.LastName);
    }

    [Fact]
    public async Task Excludes_Leavers_By_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = NewEmployee(companyId, "Anna", "Active");
        active.Activate(Now);

        var leaving = NewEmployee(companyId, "Leo", "Leaving");
        leaving.SetStatusForTesting(EmploymentStatus.Leaving, Now);

        var former = NewEmployee(companyId, "Fred", "Former");
        former.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);

        context.Employees.AddRange(active, leaving, former);
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId), CancellationToken.None);

        Assert.Equal(new[] { "Active" }, result.Value!.Items.Select(i => i.LastName).ToArray());
    }

    [Fact]
    public async Task Includes_Leavers_When_IncludeLeavers_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = NewEmployee(companyId, "Anna", "Active");
        active.Activate(Now);

        var leaving = NewEmployee(companyId, "Leo", "Leaving");
        leaving.SetStatusForTesting(EmploymentStatus.Leaving, Now);

        var former = NewEmployee(companyId, "Fred", "Former");
        former.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);

        context.Employees.AddRange(active, leaving, former);
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, includeLeavers: true), CancellationToken.None);

        Assert.Equal(
            new[] { "Active", "Former", "Leaving" },
            result.Value!.Items.Select(i => i.LastName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Active_And_Draft_Employees_Are_Always_Returned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = NewEmployee(companyId, "Anna", "Active");
        active.Activate(Now);

        // Draft = created but not yet activated (e.g. future start date)
        var future = NewEmployee(companyId, "Fiona", "Future", startDate: new DateOnly(2027, 1, 1));

        context.Employees.AddRange(active, future);
        await context.SaveChangesAsync();

        Assert.Equal(EmploymentStatus.Draft, future.Status);

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId), CancellationToken.None);

        Assert.Equal(
            new[] { "Active", "Future" },
            result.Value!.Items.Select(i => i.LastName).ToArray());
    }

    [Fact]
    public async Task Blank_Term_Returns_All_Subject_To_Leaver_Filter_And_Limit()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var a = NewEmployee(companyId, "Anna", "Adams");
        var b = NewEmployee(companyId, "Bill", "Baker");
        var leaver = NewEmployee(companyId, "Carl", "Clark");
        leaver.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);
        context.Employees.AddRange(a, b, leaver);
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "   "), CancellationToken.None);

        Assert.Equal(new[] { "Adams", "Baker" }, result.Value!.Items.Select(i => i.LastName).ToArray());
    }

    [Fact]
    public async Task Null_Term_Returns_All()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Anna", "Adams"),
            NewEmployee(companyId, "Bill", "Baker"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, term: null), CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Limit_Caps_Number_Of_Results()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
            context.Employees.Add(NewEmployee(companyId, "Emp", $"Last{i:00}"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, limit: 3), CancellationToken.None);

        Assert.Equal(3, result.Value!.Items.Count);
        // Take applies after ordering by LastName
        Assert.Equal(new[] { "Last00", "Last01", "Last02" }, result.Value.Items.Select(i => i.LastName).ToArray());
    }

    [Fact]
    public async Task Results_Are_Ordered_By_LastName_Then_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            NewEmployee(companyId, "Bob", "Smith"),
            NewEmployee(companyId, "Alice", "Smith"),
            NewEmployee(companyId, "Carol", "Jones"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId), CancellationToken.None);

        var items = result.Value!.Items;
        Assert.Equal("Jones", items[0].LastName);
        Assert.Equal("Alice", items[1].FirstName);
        Assert.Equal("Bob", items[2].FirstName);
    }

    [Fact]
    public async Task Populates_DepartmentName_And_PositionProfileTitle()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, Guid.NewGuid(), "Senior Developer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var employee = NewEmployee(companyId, "Alice", "Smith");
        employee.Assign(department.Id, profile.Id, Guid.NewGuid(), null, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "alice"), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Engineering", item.DepartmentName);
        Assert.Equal("Senior Developer", item.PositionProfileTitle);
    }

    [Fact]
    public async Task DepartmentName_And_PositionProfileTitle_Are_Null_When_Not_Resolvable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(NewEmployee(companyId, "Alice", "Smith"));
        await context.SaveChangesAsync();

        var result = await new SearchEmployeeDirectoryHandler(context)
            .HandleAsync(Request(companyId, "alice"), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.DepartmentName);
        Assert.Null(item.PositionProfileTitle);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
