using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListPositionProfiles;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListPositionProfilesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_None_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Profiles_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PositionProfiles.AddRange(
            PositionProfile.Create(Guid.NewGuid(), companyId, null, "Manager", null, true, null, now),
            PositionProfile.Create(Guid.NewGuid(), companyId, null, "Developer", null, false, null, now));
        await context.SaveChangesAsync();

        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        // Alphabetical order
        Assert.Equal("Developer", result.Value.Items[0].Title);
        Assert.Equal("Manager", result.Value.Items[1].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_Department_Name_When_Profile_Has_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        context.Departments.Add(dept);

        context.PositionProfiles.AddRange(
            PositionProfile.Create(Guid.NewGuid(), companyId, dept.Id, "Developer", null, false, null, now),
            PositionProfile.Create(Guid.NewGuid(), companyId, null, "Contractor", null, false, null, now));
        await context.SaveChangesAsync();

        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var contractor = result.Value!.Items.Single(i => i.Title == "Contractor");
        var developer = result.Value.Items.Single(i => i.Title == "Developer");
        Assert.Equal("Engineering", developer.DepartmentName);
        Assert.Null(contractor.DepartmentName);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Profiles_By_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var active = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Active Role", null, false, null, now);
        var inactive = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Old Role", null, false, null, now);
        inactive.Deactivate(now);

        context.PositionProfiles.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = companyId, IncludeInactive = false },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Active Role", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_Inactive_Profiles_When_Requested()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var active = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Active Role", null, false, null, now);
        var inactive = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Old Role", null, false, null, now);
        inactive.Deactivate(now);

        context.PositionProfiles.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = companyId, IncludeInactive = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Scopes_To_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PositionProfiles.Add(
            PositionProfile.Create(Guid.NewGuid(), companyA, null, "Engineer", null, false, null, now));
        await context.SaveChangesAsync();

        var handler = new ListPositionProfilesHandler(context);

        var result = await handler.HandleAsync(
            new ListPositionProfilesRequest { CompanyId = companyB },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
