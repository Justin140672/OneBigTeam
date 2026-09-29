using HR.Modules.Sickness.Domain;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests;

public class SicknessCalculatorTests
{
    private static readonly WorkingPattern StandardPattern = WorkingPattern.Default;

    [Fact]
    public void CalculateTotalDays_FullDay_SingleWorkingDay_Returns_One()
    {
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
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(3m, result);
    }

    [Fact]
    public void CalculateTotalDays_SpanningWeekend_ExcludesWeekend()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 6), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(4m, result);
    }

    [Fact]
    public void CalculateTotalDays_OnWeekend_Returns_Zero()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 4), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 5), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalDays_WithPublicHoliday_ExcludesIt()
    {
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

        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            pattern);

        Assert.Equal(2m, result);
    }

    [Fact]
    public void CalculateTotalDays_HalfDayStart_FullDayEnd_SpanningTwoDays()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.HalfDayPM,
            new DateOnly(2026, 7, 2), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void CalculateTotalDays_FullDayStart_HalfDayEnd_SpanningTwoDays()
    {
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
    public void CalculateTotalDays_EndDate_Before_StartDate_Returns_Zero()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 3), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalDays_ZeroHoursPerDayPattern_Returns_Zero_Without_DivideByZero()
    {
        var pattern = new WorkingPattern(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
            WorkingDays.Thursday | WorkingDays.Friday, 0m);

        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            pattern);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateTotalDays_TwoWeeks_Returns_Ten_WorkingDays()
    {
        var result = SicknessCalculator.CalculateTotalDays(
            new DateOnly(2026, 6, 29), SicknessDayPart.FullDay,
            new DateOnly(2026, 7, 10), SicknessDayPart.FullDay,
            StandardPattern);

        Assert.Equal(10m, result);
    }
}
