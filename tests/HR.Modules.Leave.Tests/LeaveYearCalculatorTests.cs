using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeaveYearCalculatorTests
{
    [Theory]
    [InlineData(2026, 1, 1, 2026)]   // Jan — same year
    [InlineData(2026, 3, 1, 2026)]   // Mar — same year
    [InlineData(2026, 12, 1, 2026)]  // Dec — same year
    public void GetPolicyYear_Returns_Calendar_Year_When_StartMonth_Is_January(
        int year, int month, int day, int expectedPolicyYear)
    {
        var date = new DateOnly(year, month, day);
        Assert.Equal(expectedPolicyYear, LeaveYearCalculator.GetPolicyYear(date, startMonth: 1));
    }

    [Theory]
    [InlineData(2026, 4, 1, 2026)]   // Apr — start of new leave year
    [InlineData(2026, 12, 31, 2026)] // Dec — still in 2026 leave year
    [InlineData(2026, 1, 1, 2025)]   // Jan — still in 2025 leave year
    [InlineData(2026, 3, 31, 2025)]  // Mar — still in 2025 leave year
    public void GetPolicyYear_Returns_Correct_Year_For_April_Start(
        int year, int month, int day, int expectedPolicyYear)
    {
        var date = new DateOnly(year, month, day);
        Assert.Equal(expectedPolicyYear, LeaveYearCalculator.GetPolicyYear(date, startMonth: 4));
    }

    [Theory]
    [InlineData(2026, 4, 1, 2025)]   // Apr — still in previous leave year
    [InlineData(2026, 5, 1, 2026)]   // May — start of new leave year
    [InlineData(2026, 1, 1, 2025)]   // Jan — still in 2025 leave year
    public void GetPolicyYear_Returns_Correct_Year_For_May_Start(
        int year, int month, int day, int expectedPolicyYear)
    {
        var date = new DateOnly(year, month, day);
        Assert.Equal(expectedPolicyYear, LeaveYearCalculator.GetPolicyYear(date, startMonth: 5));
    }

    [Fact]
    public void GetPolicyYear_DateTimeOffset_Overload_Matches_DateOnly_Overload()
    {
        var date = new DateOnly(2026, 3, 15);
        var dto = new DateTimeOffset(2026, 3, 15, 14, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            LeaveYearCalculator.GetPolicyYear(date, startMonth: 4),
            LeaveYearCalculator.GetPolicyYear(dto, startMonth: 4));
    }

    [Fact]
    public void GetPolicyYearBounds_Returns_Calendar_Year_When_StartMonth_Is_January()
    {
        var (start, end) = LeaveYearCalculator.GetPolicyYearBounds(2026, startMonth: 1);

        Assert.Equal(new DateOnly(2026, 1, 1), start);
        Assert.Equal(new DateOnly(2026, 12, 31), end);
    }

    [Fact]
    public void GetPolicyYearBounds_Returns_AprilToMarch_Bounds_For_NonCalendar_LeaveYear()
    {
        var (start, end) = LeaveYearCalculator.GetPolicyYearBounds(2026, startMonth: 4);

        Assert.Equal(new DateOnly(2026, 4, 1), start);
        Assert.Equal(new DateOnly(2027, 3, 31), end);
    }
}
