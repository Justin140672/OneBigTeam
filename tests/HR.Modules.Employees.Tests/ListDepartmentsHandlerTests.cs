using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListDepartments;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListDepartmentsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Departments()
    {
        await using var context = BuildContext();
        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Departments_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        context.Departments.AddRange(
            Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset),
            Department.Create(Guid.NewGuid(), companyId, "People", "HR team", FixedOffset),
            Department.Create(Guid.NewGuid(), otherId, "Finance", null, FixedOffset));
        await context.SaveChangesAsync();

        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Departments_By_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        var inactive = Department.Create(Guid.NewGuid(), companyId, "Legacy", null, FixedOffset);
        inactive.Deactivate(FixedOffset);

        context.Departments.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Engineering", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Includes_Inactive_When_IncludeInactive_Is_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        var inactive = Department.Create(Guid.NewGuid(), companyId, "Legacy", null, FixedOffset);
        inactive.Deactivate(FixedOffset);

        context.Departments.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = companyId, IncludeInactive = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Departments_Sorted_By_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Departments.AddRange(
            Department.Create(Guid.NewGuid(), companyId, "People", null, FixedOffset),
            Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset),
            Department.Create(Guid.NewGuid(), companyId, "Finance", null, FixedOffset));
        await context.SaveChangesAsync();

        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = companyId },
            CancellationToken.None);

        var names = result.Value!.Items.Select(i => i.Name).ToList();
        Assert.Equal(new[] { "Engineering", "Finance", "People" }, names);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var parent = Department.Create(parentId, companyId, "Engineering", null, FixedOffset);
        var child = Department.Create(Guid.NewGuid(), companyId, "Platform", "Platform sub-team", FixedOffset);
        child.Update("Platform", "Platform sub-team", parentId, null, FixedOffset);

        context.Departments.AddRange(parent, child);
        await context.SaveChangesAsync();

        var handler = new ListDepartmentsHandler(context);

        var result = await handler.HandleAsync(
            new ListDepartmentsRequest { CompanyId = companyId },
            CancellationToken.None);

        var platform = result.Value!.Items.Single(i => i.Name == "Platform");
        Assert.Equal(parentId, platform.ParentDepartmentId);
        Assert.Null(platform.ManagerEmployeeId);
        Assert.True(platform.IsActive);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
