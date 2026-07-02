using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdatePublicHoliday;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdatePublicHolidayHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    private static CompaniesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Updates_Holiday_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var holiday = PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now);
        context.PublicHolidays.Add(holiday);
        await context.SaveChangesAsync();

        var handler = new UpdatePublicHolidayHandler(context);

        var result = await handler.HandleAsync(
            new UpdatePublicHolidayRequest
            {
                CompanyId = companyId,
                Id = holiday.Id,
                Date = new DateOnly(2026, 12, 26),
                Name = "Boxing Day",
                CountryCode = "gb"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 12, 26), result.Value!.Date);
        Assert.Equal("Boxing Day", result.Value.Name);
        Assert.Equal("GB", result.Value.CountryCode);

        var saved = await context.PublicHolidays.SingleAsync();
        Assert.Equal(new DateOnly(2026, 12, 26), saved.Date);
        Assert.Equal("Boxing Day", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Holiday_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdatePublicHolidayHandler(context);

        var result = await handler.HandleAsync(
            new UpdatePublicHolidayRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Holiday_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var holiday = PublicHoliday.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now);
        context.PublicHolidays.Add(holiday);
        await context.SaveChangesAsync();

        var handler = new UpdatePublicHolidayHandler(context);

        var result = await handler.HandleAsync(
            new UpdatePublicHolidayRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = holiday.Id,
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_New_Date_Already_Taken()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var holiday1 = PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now);
        var holiday2 = PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 26), "Boxing Day", "GB", Now);
        context.PublicHolidays.AddRange(holiday1, holiday2);
        await context.SaveChangesAsync();

        var handler = new UpdatePublicHolidayHandler(context);

        var result = await handler.HandleAsync(
            new UpdatePublicHolidayRequest
            {
                CompanyId = companyId,
                Id = holiday1.Id,
                Date = new DateOnly(2026, 12, 26),
                Name = "Christmas Day",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Update_With_Same_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var holiday = PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now);
        context.PublicHolidays.Add(holiday);
        await context.SaveChangesAsync();

        var handler = new UpdatePublicHolidayHandler(context);

        var result = await handler.HandleAsync(
            new UpdatePublicHolidayRequest
            {
                CompanyId = companyId,
                Id = holiday.Id,
                Date = new DateOnly(2026, 12, 25),
                Name = "Christmas Day (Updated)",
                CountryCode = "GB"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Christmas Day (Updated)", result.Value!.Name);
    }
}
