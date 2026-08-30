using HR.Infrastructure.Email;

namespace HR.Infrastructure.Tests.Email;

public class UserAgentSummaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Returns_Unknown_For_Missing_Input(string? ua)
    {
        var summary = UserAgentSummary.Parse(ua);

        Assert.Equal("Unknown", summary.BrowserName);
        Assert.Equal("Unknown", summary.OperatingSystem);
    }

    [Theory]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome", "Windows")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
        "Edge", "Windows")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15",
        "Safari", "macOS")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Firefox", "macOS")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1",
        "Safari", "iOS")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
        "Chrome", "Android")]
    [InlineData(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome", "Linux")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0",
        "Opera", "Windows")]
    public void Parse_Detects_Friendly_Browser_And_Os(string ua, string expectedBrowser, string expectedOs)
    {
        var summary = UserAgentSummary.Parse(ua);

        Assert.Equal(expectedBrowser, summary.BrowserName);
        Assert.Equal(expectedOs, summary.OperatingSystem);
    }

    [Fact]
    public void Parse_Returns_Unknown_Browser_For_Unrecognised_Agent()
    {
        var summary = UserAgentSummary.Parse("curl/8.4.0");

        Assert.Equal("Unknown", summary.BrowserName);
        Assert.Equal("Unknown", summary.OperatingSystem);
    }
}
