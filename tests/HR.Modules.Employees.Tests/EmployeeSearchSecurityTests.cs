// SEA-08: Search security matrix — employee search cross-company isolation,
// consistent out-of-range page behaviour and search term validation.
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeSearchSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    // ── Cross-company isolation ────────────────────────────────────────────

    [Fact]
    public async Task ListEmployees_Returns_Only_Requested_Company_Records()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        ctx.Employees.AddRange(
            MakeEmployee(companyA, "Alice", "Smith"),
            MakeEmployee(companyB, "Bob",   "Jones"));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Single(result.Value.Items);
        Assert.Equal("Alice", result.Value.Items[0].FirstName);
    }

    [Fact]
    public async Task ListEmployees_TotalCount_Excludes_Other_Company_Records()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        // Company B has 5 employees; Company A has 2.
        for (var i = 0; i < 5; i++)
            ctx.Employees.Add(MakeEmployee(companyB, $"User{i}", "B"));

        ctx.Employees.AddRange(
            MakeEmployee(companyA, "One", "A"),
            MakeEmployee(companyA, "Two", "A"));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task ListEmployees_Search_Does_Not_Surface_Other_Company_Names()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        ctx.Employees.AddRange(
            MakeEmployee(companyA, "Alice", "Smith"),
            MakeEmployee(companyB, "Alice", "Jones"));  // same first name, different company
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyA, Search = "alice" },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Smith", result.Value.Items[0].LastName);
    }

    // ── Out-of-range page behaviour ────────────────────────────────────────

    [Fact]
    public async Task ListEmployees_Out_Of_Range_Page_Returns_Empty_Items_With_Correct_TotalCount()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();

        ctx.Employees.Add(MakeEmployee(companyId, "Alice", "Smith"));
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx).HandleAsync(
            new ListEmployeesRequest { CompanyId = companyId, PageNumber = 999, PageSize = 20 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    // ── Search term validation ─────────────────────────────────────────────

    [Fact]
    public void ListEmployees_Validator_Rejects_Oversized_Search_Term()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = new string('x', 201),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeesRequest.Search));
    }

    [Fact]
    public void ListEmployees_Validator_Accepts_Search_Term_At_Maximum_Length()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = new string('x', 200),
        });

        Assert.True(result.IsValid);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Employee MakeEmployee(Guid companyId, string first, string last) =>
        Employee.Create(
            Guid.NewGuid(), companyId, first, last,
            $"{first.ToLower()}.{last.ToLower()}@test.example",
            StartDate, hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    private static ListEmployeesHandler Handler(EmployeesDbContext ctx) =>
        new(ctx, new FakeProfilePhotoReader(), new FakeEmployeeUserAccountStatusReader());

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }
}
