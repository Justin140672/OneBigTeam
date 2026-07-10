using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetNewHiresTrend;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetNewHiresTrendHandlerTests
{
    // Window covers [2026-02-01, 2026-08-01) -> Feb, Mar, Apr, May, Jun, Jul 2026 (6 months).
    private static readonly DateTime FixedUtcNow = new(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ZeroFills_Six_Months_When_No_Hires()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(new GetNewHiresTrendRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(6, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(0, i.NewHireCount));

        Assert.Equal(
            [(2026, 2), (2026, 3), (2026, 4), (2026, 5), (2026, 6), (2026, 7)],
            result.Items.Select(i => (i.Year, i.Month)).ToArray());

        Assert.Equal("Feb 2026", result.Items[0].MonthLabel);
        Assert.Equal("Jul 2026", result.Items[5].MonthLabel);
    }

    [Fact]
    public async Task HandleAsync_Counts_Hires_Per_Month_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Employees.AddRange(
            NewEmployee(companyId, new DateOnly(2026, 2, 15)),
            NewEmployee(companyId, new DateOnly(2026, 2, 20)),
            NewEmployee(companyId, new DateOnly(2026, 5, 3)),
            NewEmployee(companyId, new DateOnly(2026, 7, 1)));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(new GetNewHiresTrendRequest(companyId), CancellationToken.None);

        var byMonth = result.Items.ToDictionary(i => (i.Year, i.Month), i => i.NewHireCount);
        Assert.Equal(2, byMonth[(2026, 2)]);
        Assert.Equal(0, byMonth[(2026, 3)]);
        Assert.Equal(0, byMonth[(2026, 4)]);
        Assert.Equal(1, byMonth[(2026, 5)]);
        Assert.Equal(0, byMonth[(2026, 6)]);
        Assert.Equal(1, byMonth[(2026, 7)]);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Hires_Outside_The_Six_Month_Window()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Employees.AddRange(
            NewEmployee(companyId, new DateOnly(2026, 1, 31)), // just before window start
            NewEmployee(companyId, new DateOnly(2026, 8, 1))); // just after window end
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(new GetNewHiresTrendRequest(companyId), CancellationToken.None);

        Assert.All(result.Items, i => Assert.Equal(0, i.NewHireCount));
    }

    [Fact]
    public async Task HandleAsync_Includes_Hires_On_Window_Boundaries()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Employees.AddRange(
            NewEmployee(companyId, new DateOnly(2026, 2, 1)),  // window start (inclusive)
            NewEmployee(companyId, new DateOnly(2026, 7, 31))); // window end (inclusive, last day of current month)
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(new GetNewHiresTrendRequest(companyId), CancellationToken.None);

        var byMonth = result.Items.ToDictionary(i => (i.Year, i.Month), i => i.NewHireCount);
        Assert.Equal(1, byMonth[(2026, 2)]);
        Assert.Equal(1, byMonth[(2026, 7)]);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        context.Employees.AddRange(
            NewEmployee(companyId, new DateOnly(2026, 6, 1)),
            NewEmployee(otherCompanyId, new DateOnly(2026, 6, 1)));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(new GetNewHiresTrendRequest(companyId), CancellationToken.None);

        var byMonth = result.Items.ToDictionary(i => (i.Year, i.Month), i => i.NewHireCount);
        Assert.Equal(1, byMonth[(2026, 6)]);
        Assert.Equal(1, result.Items.Sum(i => i.NewHireCount));
    }

    private static Employee NewEmployee(Guid companyId, DateOnly startDate) =>
        Employee.Create(
            Guid.NewGuid(), companyId, "First", "Last",
            $"employee.{Guid.NewGuid():N}@example.com",
            startDate, hasSystemAccess: true, Now);

    private static GetNewHiresTrendHandler BuildHandler(EmployeesDbContext context) =>
        new(context, new FakeClock(FixedUtcNow));

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
