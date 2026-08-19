using HR.Modules.Leave.Domain;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Leave.Tests;

public class LeaveCalculatorTests
{
    // Monday=2026-08-03, Tuesday=04, Wednesday=05, Thursday=06, Friday=07
    // Saturday=2026-08-08, Sunday=2026-08-09

    [Theory]
    [InlineData("2026-08-03", "FullDay",    "2026-08-05", "FullDay",    3.0)]  // Mon–Wed full = 3 days
    [InlineData("2026-08-03", "FullDay",    "2026-08-07", "FullDay",    5.0)]  // Mon–Fri full = 5 days
    [InlineData("2026-08-03", "FullDay",    "2026-08-03", "FullDay",    1.0)]  // Single day full = 1 day
    [InlineData("2026-08-03", "Morning",    "2026-08-03", "Morning",    0.5)]  // Single day morning = 0.5
    [InlineData("2026-08-03", "Afternoon",  "2026-08-03", "Afternoon",  0.5)]  // Single day afternoon = 0.5
    [InlineData("2026-08-03", "Morning",    "2026-08-05", "Afternoon",  2.0)]  // Mon morning + Tue full + Wed afternoon = 0.5+1+0.5
    [InlineData("2026-08-03", "FullDay",    "2026-08-10", "FullDay",    6.0)]  // Mon–Mon spanning weekend = 6 working days
    [InlineData("2026-08-08", "FullDay",    "2026-08-09", "FullDay",    0.0)]  // Sat–Sun = 0 working days (default Mon–Fri)
    [InlineData("2026-08-07", "Morning",    "2026-08-10", "Afternoon",  1.0)]  // Fri morning + Mon afternoon = 0.5+0.5 (weekend skipped)
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

        // 2026-08-08 = Saturday
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

        // 2026-08-08 = Saturday, 2026-08-09 = Sunday
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

        // 2026-08-08 = Saturday, 2026-08-09 = Sunday — only Saturday counts
        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 8), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 9), LeaveDayPart.FullDay,
            satOnly);

        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void CalculateTotalDays_Excludes_Public_Holiday_On_Working_Day()
    {
        var holiday = new DateOnly(2026, 8, 5); // Wednesday

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
        var holiday = new DateOnly(2026, 8, 8); // Saturday — not a working day anyway

        var result = LeaveCalculator.CalculateTotalDays(
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 10), LeaveDayPart.FullDay,
            WorkingPattern.Default,
            publicHolidays: [holiday]);

        Assert.Equal(6.0m, result); // Mon–Mon = 6 days, Sat holiday has no effect
    }
}
