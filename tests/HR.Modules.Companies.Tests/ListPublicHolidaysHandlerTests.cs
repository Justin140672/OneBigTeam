using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.ListPublicHolidays;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class ListPublicHolidaysHandlerTests
{
    private static CompaniesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_Holidays_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1),  "New Year's Day",      "GB", Now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day",       "GB", Now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2027, 1, 1),  "New Year's Day 2027", "GB", Now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context);
        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Holidays_Ordered_By_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1),   "New Year's Day", "GB", Now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 4, 3),   "Good Friday",    "GB", Now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context);
        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new DateOnly(2026, 1, 1),   result.Items[0].Date);
        Assert.Equal(new DateOnly(2026, 4, 3),   result.Items[1].Date);
        Assert.Equal(new DateOnly(2026, 12, 25), result.Items[2].Date);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Holidays_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListPublicHolidaysHandler(context);

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Only_Returns_Holidays_For_Requested_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyA, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now),
            PublicHoliday.Create(Guid.NewGuid(), companyB, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context);
        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(companyA, result.Items[0].CompanyId);
    }
}
