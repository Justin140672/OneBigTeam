using HR.Modules.Companies.Services;

namespace HR.Modules.Companies.Tests;

public class UkPublicHolidayCalendarTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void EasterSunday_Matches_Known_Dates(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), UkPublicHolidayCalendar.EasterSunday(year));
    }

    [Fact]
    public void Year2026_Matches_Existing_Seed_Data()
    {
        var holidays = UkPublicHolidayCalendar.GetEnglandAndWales(2026);

        Assert.Equal(
            new (DateOnly, string)[]
            {
                (new DateOnly(2026, 1, 1), "New Year's Day"),
                (new DateOnly(2026, 4, 3), "Good Friday"),
                (new DateOnly(2026, 4, 6), "Easter Monday"),
                (new DateOnly(2026, 5, 4), "Early May Bank Holiday"),
                (new DateOnly(2026, 5, 25), "Spring Bank Holiday"),
                (new DateOnly(2026, 8, 31), "Summer Bank Holiday"),
                (new DateOnly(2026, 12, 25), "Christmas Day"),
                (new DateOnly(2026, 12, 28), "Boxing Day (substitute)")
            },
            holidays.ToArray());
    }

    [Fact]
    public void ChristmasOnSaturday_Substitutes_Monday_And_Tuesday()
    {
        var holidays = UkPublicHolidayCalendar.GetEnglandAndWales(2027);

        Assert.Contains((new DateOnly(2027, 12, 27), "Christmas Day (substitute)"), holidays);
        Assert.Contains((new DateOnly(2027, 12, 28), "Boxing Day (substitute)"), holidays);
    }

    [Fact]
    public void ChristmasOnSunday_Substitutes_Tuesday_And_Keeps_Boxing_Day_On_Monday()
    {
        var holidays = UkPublicHolidayCalendar.GetEnglandAndWales(2022);

        Assert.Contains((new DateOnly(2022, 12, 26), "Boxing Day"), holidays);
        Assert.Contains((new DateOnly(2022, 12, 27), "Christmas Day (substitute)"), holidays);
    }

    [Theory]
    [InlineData(2023, 1, 2)]
    [InlineData(2028, 1, 3)]
    public void NewYearsDay_On_Weekend_Substitutes_Following_Monday(int year, int month, int day)
    {
        var holidays = UkPublicHolidayCalendar.GetEnglandAndWales(year);

        Assert.Contains((new DateOnly(year, month, day), "New Year's Day (substitute)"), holidays);
    }

    [Fact]
    public void GetEnglandAndWales_Never_Returns_Weekend_Or_Duplicate_Dates()
    {
        for (var year = 2020; year <= 2060; year++)
        {
            var holidays = UkPublicHolidayCalendar.GetEnglandAndWales(year);

            Assert.Equal(8, holidays.Count);
            Assert.Equal(holidays.Count, holidays.Select(h => h.Date).Distinct().Count());
            Assert.All(holidays, h => Assert.DoesNotContain(h.Date.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
        }
    }

    [Fact]
    public void GetUpcoming_Excludes_Holiday_Falling_On_Today()
    {
        var upcoming = UkPublicHolidayCalendar.GetUpcoming(new DateOnly(2026, 12, 25), 1);

        Assert.DoesNotContain(upcoming, h => h.Date <= new DateOnly(2026, 12, 25));
        Assert.Equal(new DateOnly(2026, 12, 28), upcoming[0].Date);
    }

    [Fact]
    public void GetUpcoming_Includes_Holiday_Falling_Tomorrow()
    {
        var upcoming = UkPublicHolidayCalendar.GetUpcoming(new DateOnly(2026, 12, 24), 0);

        Assert.Equal((new DateOnly(2026, 12, 25), "Christmas Day"), upcoming[0]);
    }

    [Fact]
    public void GetUpcoming_Covers_Rest_Of_This_Year_And_All_Of_Next()
    {
        var upcoming = UkPublicHolidayCalendar.GetUpcoming(new DateOnly(2026, 9, 29), 1);

        Assert.Equal(new DateOnly(2026, 12, 25), upcoming[0].Date);
        Assert.Equal(new DateOnly(2027, 12, 28), upcoming[^1].Date);
        Assert.Equal(10, upcoming.Count);
    }
}
