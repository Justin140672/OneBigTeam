using HR.Modules.Leave.Domain;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Leave.Tests;

public class LeaveCalculatorTests
{

    [Theory]
    [InlineData("2026-08-03", "FullDay",    "2026-08-05", "FullDay",    3.0)]
    [InlineData("2026-08-03", "FullDay",    "2026-08-07", "FullDay",    5.0)]
    [InlineData("2026-08-03", "FullDay",    "2026-08-03", "FullDay",    1.0)]
    [InlineData("2026-08-03", "Morning",    "2026-08-03", "Morning",    0.5)]
    [InlineData("2026-08-03", "Afternoon",  "2026-08-03", "Afternoon",  0.5)]
    [InlineData("2026-08-03", "Morning",    "2026-08-05", "Afternoon",  2.0)]
    [InlineData("2026-08-03", "FullDay",    "2026-08-10", "FullDay",    6.0)]
    [InlineData("2026-08-08", "FullDay",    "2026-08-09", "FullDay",    0.0)]
    [InlineData("2026-08-07", "Morning",    "2026-08-10", "Afternoon",  1.0)]
    public void CalculateTotalDays_Returns_Correct_Value_For_Standard_Mon_Fri_Pattern(
        string startDate, string startPart, string endDate, string endPart, decimal expected)
    {
        var result = LeaveCalculator.CalculateTotalDays(
            DateOnly.Parse(startDate),
            Enum.Parse<LeaveDayPart>(startPart),
            DateOnly.Parse(endDate),
            Enum.Parse<LeaveDayPart>(endPart),
            WorkingPattern.Default);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateTotalDays_Counts_Saturday_When_Saturday_Is_In_Working_Pattern()
    {
        var monToSat = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
            WorkingDays.Thursday | WorkingDays.Friday | WorkingDays.Saturday,
            7.5m);

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 8), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 8), LeaveDayPart.FullDay,
            monToSat);

        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void CalculateTotalDays_Counts_Both_Weekend_Days_When_Full_Week_Pattern_Set()
    {
        var allWeek = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
            WorkingDays.Thursday | WorkingDays.Friday | WorkingDays.Saturday | WorkingDays.Sunday,
            7.5m);

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 8), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 9), LeaveDayPart.FullDay,
            allWeek);

        Assert.Equal(2.0m, result);
    }

    [Fact]
    public void CalculateTotalDays_Skips_Sunday_When_Only_Saturday_Is_Working_Day()
    {
        var satOnly = new WorkingPattern(WorkingDays.Saturday, 7.5m);

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 8), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 9), LeaveDayPart.FullDay,
            satOnly);

        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void CalculateTotalDays_Excludes_Public_Holiday_On_Working_Day()
    {
        var holiday = new DateOnly(2026, 8, 5);

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            WorkingPattern.Default,
            publicHolidays: [holiday]);

        Assert.Equal(4.0m, result);
    }

    [Fact]
    public void CalculateTotalDays_Does_Not_Reduce_Days_For_Holiday_On_Non_Working_Day()
    {
        var holiday = new DateOnly(2026, 8, 8);

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 10), LeaveDayPart.FullDay,
            WorkingPattern.Default,
            publicHolidays: [holiday]);

        Assert.Equal(6.0m, result);
    }
}
