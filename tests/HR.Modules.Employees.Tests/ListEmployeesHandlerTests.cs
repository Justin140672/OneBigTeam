using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListEmployeesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        await using var context = BuildContext();
        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Employees_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.AddRange(
            Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Carol", "Other", "carol@other.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Equal(companyId, item.CompanyId));
    }

    [Fact]
    public async Task HandleAsync_Results_Are_Ordered_By_LastName_Then_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.AddRange(
            Employee.Create(Guid.NewGuid(), companyId, "Bob", "Smith", "bob@example.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), companyId, "Carol", "Jones", "carol@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal("Jones", items[0].LastName);
        Assert.Equal("Alice", items[1].FirstName);  // Smith, Alice before Smith, Bob
        Assert.Equal("Bob", items[2].FirstName);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Search_On_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.AddRange(
            Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Search = "ali" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Alice", result.Value.Items[0].FirstName);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Search_On_WorkEmail()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.AddRange(
            Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@acme.com", StartDate, hasSystemAccess: true, now),
            Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@globex.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Search = "acme" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("alice@acme.com", result.Value.Items[0].WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DepartmentId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var departmentId = Guid.NewGuid();

        var emp1 = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        emp1.Assign(departmentId, null, null, now);
        var emp2 = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(emp1, emp2);
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, DepartmentId = departmentId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(departmentId, result.Value.Items[0].DepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Department_PositionProfile_And_Manager_Names()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Software Developer", null, null, null, null, null, null, null, null, now);
        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(profile);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.Assign(department.Id, profile.Id, manager.Id, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Search = "alice" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = result.Value!.Items.Single();
        Assert.Equal("Engineering", item.DepartmentName);
        Assert.Equal("Software Developer", item.PositionProfileTitle);
        Assert.Equal("Jane Manager", item.ManagerFullName);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var active = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        active.Activate(now);
        var draft = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(active, draft);
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var result = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Status = EmploymentStatus.Active },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(EmploymentStatus.Active, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Pages_Results_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        for (var i = 0; i < 5; i++)
        {
            context.Employees.Add(
                Employee.Create(Guid.NewGuid(), companyId, "Employee", $"Z{i:00}", $"emp{i}@example.com", StartDate, hasSystemAccess: true, now));
        }
        await context.SaveChangesAsync();

        var handler = new ListEmployeesHandler(context);

        var page1 = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, PageNumber = 1, PageSize = 2 },
            CancellationToken.None);

        var page2 = await handler.HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(3, page1.Value.TotalPages);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(2, page2.Value!.Items.Count);

        // No overlap between pages
        var page1Ids = page1.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(page2.Value.Items, i => page1Ids.Contains(i.Id));
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
