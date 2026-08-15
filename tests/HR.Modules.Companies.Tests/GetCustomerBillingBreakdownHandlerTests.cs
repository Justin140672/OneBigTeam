using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCustomerBillingBreakdown;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class GetCustomerBillingBreakdownHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Some Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Computes_Breakdown_And_Persists_Snapshot()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Billing Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var directoryReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 0 };
        var starterReader = new FakeEmployeeStarterReader { TotalCountToReturn = 2 };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            directoryReader,
            starterReader,
            monthlyPriceGbp: 10m);

        var result = await handler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(company.Id, value.CompanyId);
        Assert.Equal(2, value.FutureStarters);
        Assert.Equal(10m, value.PricePerEmployee);
        Assert.Equal(0m, value.Discounts);
        Assert.Single(value.History);

        var persisted = await context.CustomerBillingSnapshots
            .Where(s => s.CompanyId == company.Id)
            .ToListAsync();
        Assert.Single(persisted);
        Assert.Equal(value.ActiveEmployees, persisted[0].ActiveEmployees);
        Assert.Equal(value.Leavers, persisted[0].Leavers);
        Assert.Equal(value.ChargeableEmployees, persisted[0].ChargeableEmployees);
        Assert.Equal(value.MonthlyTotal, persisted[0].MonthlyTotal);
    }

    [Fact]
    public async Task HandleAsync_Computes_ChargeableEmployees_As_Active_Plus_Leavers_And_MonthlyTotal()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Chargeable Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        // The fake directory reader returns the same TotalCountToReturn regardless of the
        // EmployeeStatus filter, so ActiveEmployees and Leavers end up equal in this test — the
        // important assertion is that ChargeableEmployees = ActiveEmployees + Leavers and
        // MonthlyTotal = ChargeableEmployees * PricePerEmployee (Discounts is always 0 today).
        var directoryReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 4 };
        var starterReader = new FakeEmployeeStarterReader { TotalCountToReturn = 0 };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            directoryReader,
            starterReader,
            monthlyPriceGbp: 25m);

        var result = await handler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(4, value.ActiveEmployees);
        Assert.Equal(4, value.Leavers);
        Assert.Equal(8, value.ChargeableEmployees);
        Assert.Equal(200m, value.MonthlyTotal);
    }

    [Fact]
    public async Task HandleAsync_Called_Twice_Accumulates_History_Ordered_By_ComputedAt_Descending()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "History Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var directoryReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 1 };
        var starterReader = new FakeEmployeeStarterReader { TotalCountToReturn = 0 };

        var firstHandler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            directoryReader,
            starterReader,
            monthlyPriceGbp: 10m,
            clock: new FakeClock(Now));

        var firstResult = await firstHandler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(company.Id), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondHandler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            directoryReader,
            starterReader,
            monthlyPriceGbp: 10m,
            clock: new FakeClock(Now.AddHours(1)));

        var secondResult = await secondHandler.HandleAsync(
            new GetCustomerBillingBreakdownRequest(company.Id), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        var history = secondResult.Value!.History;
        Assert.True(history.Count >= 2);
        Assert.True(history[0].ComputedAt >= history[1].ComputedAt);

        var persisted = await context.CustomerBillingSnapshots
            .Where(s => s.CompanyId == company.Id)
            .ToListAsync();
        Assert.Equal(2, persisted.Count);
    }

    private static GetCustomerBillingBreakdownHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeEmployeeDirectoryReader? employeeDirectoryReader = null,
        FakeEmployeeStarterReader? employeeStarterReader = null,
        decimal monthlyPriceGbp = 49m,
        FakeClock? clock = null)
    {
        return new GetCustomerBillingBreakdownHandler(
            context,
            currentUser,
            configuration,
            employeeDirectoryReader ?? new FakeEmployeeDirectoryReader(),
            employeeStarterReader ?? new FakeEmployeeStarterReader(),
            Options.Create(new StripeOptions { MonthlyPriceGbp = monthlyPriceGbp }),
            clock ?? new FakeClock(Now));
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            builder.AddInMemoryCollection(data);
        }
        else
        {
            builder.AddInMemoryCollection();
        }

        return builder.Build();
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
