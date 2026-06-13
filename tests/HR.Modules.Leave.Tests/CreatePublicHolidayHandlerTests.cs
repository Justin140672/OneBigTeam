using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CreatePublicHoliday;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class CreatePublicHolidayHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Creates_Holiday_And_Returns_Response()
    {
        await using var context = BuildContext();
        var handler = new CreatePublicHolidayHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreatePublicHolidayRequest
            {
                CompanyId = companyId,
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day",
                CountryCode = "gb"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(new DateOnly(2026, 12, 25), result.Value.Date);
        Assert.Equal("Christmas Day", result.Value.Name);
        Assert.Equal("GB", result.Value.CountryCode);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.CreatedAt);

        var saved = await context.PublicHolidays.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Same_Date_Exists_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.Add(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now));
        await context.SaveChangesAsync();

        var handler = new CreatePublicHolidayHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreatePublicHolidayRequest
            {
                CompanyId = companyId,
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Date_For_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.PublicHolidays.Add(
            PublicHoliday.Create(Guid.NewGuid(), companyA, new DateOnly(2026, 12, 25), "Christmas Day", "GB", now));
        await context.SaveChangesAsync();

        var handler = new CreatePublicHolidayHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreatePublicHolidayRequest
            {
                CompanyId = companyB,
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Normalises_CountryCode_To_Uppercase()
    {
        await using var context = BuildContext();
        var handler = new CreatePublicHolidayHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreatePublicHolidayRequest
            {
                CompanyId = Guid.NewGuid(),
                Date = new DateOnly(2026, 1, 1),
                Name = "New Year's Day",
                CountryCode = "gb"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GB", result.Value!.CountryCode);
    }
}
