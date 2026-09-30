using System.Globalization;
using HR.Web.Services;

namespace HR.Web.Tests;

public class SalaryRangeFormatterTests
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    [Theory]
    [InlineData(45000, 55000, "Annual", "£45,000–£55,000 per year")]
    [InlineData(45000, null, "Annual", "From £45,000 per year")]
    [InlineData(null, 55000, "Annual", "Up to £55,000 per year")]
    [InlineData(20, 30, "Hourly", "£20–£30 per hour")]
    [InlineData(150, null, "Daily", "From £150 per day")]
    [InlineData(45000, 55000, null, "£45,000–£55,000 per year")]
    public void Format_Returns_Expected_Text(int? min, int? max, string? type, string expected)
    {
        Assert.Equal(expected, SalaryRangeFormatter.Format(min, max, type, Uk));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Annual")]
    public void Format_Returns_Null_When_No_Bounds(string? type)
    {
        Assert.Null(SalaryRangeFormatter.Format(null, null, type, Uk));
    }
}
