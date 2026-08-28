using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Covers the ManagerId, LocationId and extended-search (job title / dept name) filters
/// added in SEA-01.
/// </summary>
public class ListEmployeesFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    // ── ManagerId filter ──────────────────────────────────────────────────

    [Fact]
    public async Task Filter_By_ManagerId_Returns_Only_That_Managers_Reports()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var deptId     = Guid.NewGuid();
        var locId      = Guid.NewGuid();
        var posId      = Guid.NewGuid();
        var typeId     = Guid.NewGuid();

        var report1 = MakeEmployee(ctx, companyId, "Alice", deptId, locId, posId, typeId, managerId: managerId);
        var report2 = MakeEmployee(ctx, companyId, "Bob",   deptId, locId, posId, typeId, managerId: managerId);
        var other   = MakeEmployee(ctx, companyId, "Carol", deptId, locId, posId, typeId, managerId: Guid.NewGuid());
        ctx.Employees.AddRange(report1, report2, other);
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, ManagerId = managerId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, i => Assert.Equal(managerId, i.ManagerId));
    }

    [Fact]
    public async Task Filter_By_ManagerId_Returns_Empty_When_No_Reports()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var deptId    = Guid.NewGuid();
        var locId     = Guid.NewGuid();
        var posId     = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        ctx.Employees.Add(MakeEmployee(ctx, companyId, "Dave", deptId, locId, posId, typeId, managerId: Guid.NewGuid()));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    // ── LocationId filter ─────────────────────────────────────────────────

    [Fact]
    public async Task Filter_By_LocationId_Returns_Only_Employees_At_That_Location()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var locA      = Guid.NewGuid();
        var locB      = Guid.NewGuid();
        var deptId    = Guid.NewGuid();
        var posId     = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        ctx.Employees.AddRange(
            MakeEmployee(ctx, companyId, "Eve",   deptId, locA, posId, typeId),
            MakeEmployee(ctx, companyId, "Frank", deptId, locA, posId, typeId),
            MakeEmployee(ctx, companyId, "Grace", deptId, locB, posId, typeId));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, LocationId = locA },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, i => Assert.Equal(locA, i.LocationId));
    }

    // ── Search extended to job title ──────────────────────────────────────

    [Fact]
    public async Task Search_By_JobTitle_Returns_Matching_Employees()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var deptId    = Guid.NewGuid();
        var locId     = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        var posEngineer  = Guid.NewGuid();
        var posDesigner  = Guid.NewGuid();

        ctx.PositionProfiles.AddRange(
            MakePositionProfile(posEngineer, companyId, deptId, locId, "Senior Engineer"),
            MakePositionProfile(posDesigner, companyId, deptId, locId, "UX Designer"));

        ctx.Employees.AddRange(
            MakeEmployee(ctx, companyId, "Harry", deptId, locId, posEngineer, typeId),
            MakeEmployee(ctx, companyId, "Irene", deptId, locId, posEngineer, typeId),
            MakeEmployee(ctx, companyId, "Jack",  deptId, locId, posDesigner, typeId));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Search = "engineer" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    // ── Search extended to department name ────────────────────────────────

    [Fact]
    public async Task Search_By_DepartmentName_Returns_Matching_Employees()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var locId      = Guid.NewGuid();
        var posId      = Guid.NewGuid();
        var typeId     = Guid.NewGuid();

        var deptEng    = Guid.NewGuid();
        var deptFinance = Guid.NewGuid();

        ctx.Departments.AddRange(
            MakeDepartment(deptEng,     companyId, "Engineering"),
            MakeDepartment(deptFinance, companyId, "Finance"));

        ctx.PositionProfiles.Add(MakePositionProfile(posId, companyId, deptEng, locId, "Developer"));

        ctx.Employees.AddRange(
            MakeEmployee(ctx, companyId, "Kate",  deptEng,     locId, posId, typeId),
            MakeEmployee(ctx, companyId, "Leo",   deptEng,     locId, posId, typeId),
            MakeEmployee(ctx, companyId, "Mia",   deptFinance, locId, posId, typeId));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, Search = "engineering" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ListEmployeesHandler Handler(EmployeesDbContext ctx) =>
        new(ctx, new FakeProfilePhotoReader(), new FakeEmployeeUserAccountStatusReader());

    private static Employee MakeEmployee(
        EmployeesDbContext ctx,
        Guid companyId, string firstName,
        Guid deptId, Guid locId, Guid posId, Guid typeId,
        Guid? managerId = null)
    {
        var emp = Employee.Create(
            Guid.NewGuid(), companyId, firstName, "Test", $"{firstName.ToLower()}@example.com",
            StartDate, hasSystemAccess: false, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            typeId, deptId, locId, posId, Now);

        if (managerId.HasValue)
            emp.Assign(deptId, posId, locId, managerId, Now);

        return emp;
    }

    private static Department MakeDepartment(Guid id, Guid companyId, string name) =>
        Department.Create(id, companyId, name, null, Now);

    private static PositionProfile MakePositionProfile(Guid id, Guid companyId, Guid deptId, Guid locId, string title) =>
        PositionProfile.Create(id, companyId, deptId, locId, title, null, null, null, null, null, null, null, Guid.NewGuid(), Now);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
