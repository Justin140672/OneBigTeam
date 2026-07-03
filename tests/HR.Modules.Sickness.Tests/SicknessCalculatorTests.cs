using HR.Modules.Sickness.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests;

public class SicknessCalculatorTests
{
    // Default Mon-Fri, 7.5h/day
    private static readonly WorkingPattern StandardPattern = WorkingPattern.Default;

    [Fact]
    public void CalculateTotalDays_FullDay_SingleWorkingDay_Returns_One()
    {
        // 2026-07-01 is a Wednesday
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(1m, result);
    }

    [Fact]
    public void CalculateTotalDays_HalfDayAM_SingleWorkingDay_Returns_Half()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayAM,
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayAM,
            StandardPattern);

        Assert.Equal(0.5m, result);
    }

    [Fact]
    public void CalculateTotalDays_HalfDayPM_SingleWorkingDay_Returns_Half()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayPM,
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayPM,
            StandardPattern);

        Assert.Equal(0.5m, result);
    }

    [Fact]
    public void CalculateTotalDays_ThreeConsecutiveWorkingDays_Returns_Three()
    {
        // Wed to Fri = 3 days
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(3m, result);
    }

    [Fact]
    public void CalculateTotalDays_SpanningWeekend_ExcludesWeekend()
    {
        // Wed 1 Jul to Mon 6 Jul = 4 working days (Wed, Thu, Fri, Mon)
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 6), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(4m, result);
    }

    [Fact]
    public void CalculateTotalDays_OnWeekend_Returns_Zero()
    {
        // 2026-07-04 is Saturday, 2026-07-05 is Sunday
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 4), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 5), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalDays_WithPublicHoliday_ExcludesIt()
    {
        // Wed 1 Jul to Fri 3 Jul, but Thu 2 Jul is a public holiday → 2 days
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            StandardPattern,
            publicHolidays);

        Assert.Equal(2m, result);
    }

    [Fact]
    public void CalculateTotalDays_WithoutPublicHolidayList_CountsHolidayAsWorkingDay()
    {
        // No public holiday list passed — the day counts
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            StandardPattern,
            null);

        Assert.Equal(3m, result);
    }

    [Fact]
    public void CalculateTotalDays_CustomFourDayPattern_ExcludesFriday()
    {
        var pattern = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
            8m);

        // Wed 1 Jul to Fri 3 Jul — Fri is not a working day in this pattern
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            pattern);

        Assert.Equal(2m, result); // Wed + Thu
    }

    [Fact]
    public void CalculateTotalDays_HalfDayStart_FullDayEnd_SpanningTwoDays()
    {
        // Start HalfDayPM on Wed, FullDay on Thu
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayPM,
            new DateOnly(2026, 7, 2), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void CalculateTotalDays_FullDayStart_HalfDayEnd_SpanningTwoDays()
    {
        // FullDay Wed, HalfDayAM Thu
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 2), SicknessDayPart.HalfDayAM,
            StandardPattern);

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void CalculateTotalDays_AllDaysArePublicHolidays_Returns_Zero()
    {
        var publicHolidays = new List<DateOnly>
        {
            new(2026, 7, 1),
            new(2026, 7, 2),
            new(2026, 7, 3)
        };

        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            StandardPattern,
            publicHolidays);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalDays_TwoWeeks_Returns_Ten_WorkingDays()
    {
        // Mon 29 Jun to Fri 10 Jul 2026 = 10 working days
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 6, 29), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 10), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(10m, result);
    }
}
