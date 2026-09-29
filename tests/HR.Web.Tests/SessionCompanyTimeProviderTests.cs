using HR.Web.Services;

namespace HR.Web.Tests;

public class SessionCompanyTimeProviderTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0, int s = 0) =>
        new(y, mo, d, h, mi, s, TimeSpan.Zero);

    [Fact]
    public void Utc_Zone_Returns_Utc_Date() =>
        Assert.Equal(new DateOnly(2026, 1, 15), SessionCompanyTimeProvider.TodayIn("UTC", Utc(2026, 1, 15, 23, 59)));

    [Theory]
    [InlineData("Pacific/Auckland", 2026, 1, 1, 23, 0, 2026, 1, 2)]   // UTC+13 in NZ summer: next day
    [InlineData("Pacific/Auckland", 2026, 1, 1, 10, 59, 2026, 1, 1)]  // 23:59 local
    [InlineData("Pacific/Auckland", 2026, 1, 1, 11, 0, 2026, 1, 2)]   // 00:00 local
    [InlineData("America/Los_Angeles", 2026, 1, 1, 7, 59, 2025, 12, 31)] // 23:59 local previous day
    [InlineData("America/Los_Angeles", 2026, 1, 1, 8, 0, 2026, 1, 1)]    // 00:00 local
    [InlineData("Asia/Kolkata", 2026, 1, 1, 18, 30, 2026, 1, 2)]      // UTC+5:30
    public void NonUtc_Zone_Converts_To_Company_Local_Date(string tz, int y, int mo, int d, int h, int mi, int ey, int emo, int ed) =>
        Assert.Equal(new DateOnly(ey, emo, ed), SessionCompanyTimeProvider.TodayIn(tz, Utc(y, mo, d, h, mi)));

    [Theory]
    // UK spring forward 2026-03-29 01:00 UTC: London becomes UTC+1, so 23:30 UTC on 28 Mar is still 28 Mar (GMT)
    [InlineData(2026, 3, 28, 23, 30, 2026, 3, 28)]
    // 23:30 UTC on 29 Mar is 00:30 BST on 30 Mar
    [InlineData(2026, 3, 29, 23, 30, 2026, 3, 30)]
    // UK fall back 2026-10-25 01:00 UTC: 23:30 UTC on 24 Oct is 00:30 BST on 25 Oct
    [InlineData(2026, 10, 24, 23, 30, 2026, 10, 25)]
    // after fall back, 23:30 UTC on 25 Oct is 23:30 GMT, still 25 Oct
    [InlineData(2026, 10, 25, 23, 30, 2026, 10, 25)]
    public void Dst_Boundaries_Use_The_Offset_In_Force_At_That_Instant(int y, int mo, int d, int h, int mi, int ey, int emo, int ed) =>
        Assert.Equal(new DateOnly(ey, emo, ed), SessionCompanyTimeProvider.TodayIn("Europe/London", Utc(y, mo, d, h, mi)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A_Zone")]
    public void Missing_Or_Invalid_Zone_Falls_Back_To_Utc(string? tz)
    {
        Assert.Equal(TimeZoneInfo.Utc, SessionCompanyTimeProvider.ResolveTimeZone(tz));
        Assert.Equal(new DateOnly(2026, 1, 1), SessionCompanyTimeProvider.TodayIn(tz, Utc(2026, 1, 1, 23, 30)));
    }

    [Fact]
    public void Resolves_Iana_Zone_Id() =>
        Assert.Equal("Europe/London", SessionCompanyTimeProvider.ResolveTimeZone("Europe/London").Id);
}
