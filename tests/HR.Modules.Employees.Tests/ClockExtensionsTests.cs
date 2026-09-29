using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class ClockExtensionsTests
{
    [Fact]
    public void TodayIn_Returns_Later_Local_Day_Across_A_BST_Transition_Boundary()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc));

        var localToday = clock.TodayIn("Europe/London");

        Assert.Equal(new DateOnly(2026, 3, 30), localToday);
        Assert.NotEqual(DateOnly.FromDateTime(clock.UtcNow), localToday);
    }

    [Fact]
    public void TodayIn_Falls_Back_To_Utc_Day_For_Unknown_TimeZoneId()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc));

        var today = clock.TodayIn("Not/ARealTimeZone");

        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow), today);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TodayIn_Falls_Back_To_Utc_Day_For_Null_Or_Empty_TimeZoneId(string? timeZoneId)
    {
        var clock = new FakeClock(new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc));

        var today = clock.TodayIn(timeZoneId);

        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow), today);
    }
}
