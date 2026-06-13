using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ListPublicHolidays;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ListPublicHolidaysHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_Holidays_For_Requested_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), "New Year's Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2027, 1, 1), "New Year's Day 2027", "GB", now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyId, Year = 2026 },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(2026, item.Date.Year));
    }

    [Fact]
    public async Task HandleAsync_Returns_Holidays_Ordered_By_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), "New Year's Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 4, 3), "Good Friday", "GB", now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyId, Year = 2026 },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Items[0].Date);
        Assert.Equal(new DateOnly(2026, 4, 3), result.Items[1].Date);
        Assert.Equal(new DateOnly(2026, 12, 25), result.Items[2].Date);
    }

    [Fact]
    public async Task HandleAsync_Uses_Current_Year_When_Year_Is_Zero()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2025, 12, 25), "Christmas Day 2025", "GB", now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyId, Year = 0 },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(2026, result.Items[0].Date.Year);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Holidays_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListPublicHolidaysHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = Guid.NewGuid(), Year = 2026 },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Only_Returns_Holidays_For_Requested_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.AddRange(
            PublicHoliday.Create(Guid.NewGuid(), companyA, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now),
            PublicHoliday.Create(Guid.NewGuid(), companyB, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now));
        await context.SaveChangesAsync();

        var handler = new ListPublicHolidaysHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new ListPublicHolidaysRequest { CompanyId = companyA, Year = 2026 },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(companyA, result.Items[0].CompanyId);
    }
}
