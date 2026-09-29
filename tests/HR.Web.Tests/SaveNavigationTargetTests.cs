using HR.SharedKernel.Http;
using HR.Web.Services;

namespace HR.Web.Tests;

public sealed class SaveNavigationTargetTests
{
    private const string Edit = "http://localhost/companies/abc/edit";

    [Theory]
    [InlineData(null, "/companies/abc/edit", true)]
    [InlineData("", "/companies/abc/edit", true)]
    [InlineData(null, "/dashboard/hr", false)]
    [InlineData("/getting-started", "/companies/abc/edit", false)]
    [InlineData("/companies/abc/edit", "/dashboard/hr", true)]
    [InlineData("/Companies/ABC/edit/?x=1#top", "/dashboard/hr", true)]
    [InlineData("/companies/abc/edit-other", "/dashboard/hr", false)]
    public void IsSamePage_ResolvesTargetFromReturnUrlThenLanding(string? returnUrl, string landing, bool expected)
    {
        Assert.Equal(expected, SaveNavigationTarget.IsSamePage(returnUrl, landing, Edit + "?returnUrl=%2F"));
    }

    [Theory]
    [InlineData("/getting-started", "/getting-started")]
    [InlineData("/companies/1/employees?a=b", "/companies/1/employees?a=b")]
    [InlineData("//evil.example", null)]
    [InlineData("https://evil.example", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("evil", null)]
    [InlineData("", null)]
    public void ReturnUrlValidator_AcceptsOnlyLocalPaths(string input, string? expected)
    {
        Assert.Equal(expected, ReturnUrlValidator.ValidateInternalPath(input));
    }
}
