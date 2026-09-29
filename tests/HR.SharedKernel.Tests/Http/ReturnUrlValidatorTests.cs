using HR.SharedKernel.Http;
using Xunit;

namespace HR.SharedKernel.Tests.Http;

public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData("/getting-started")]
    [InlineData("/page?query=1")]
    [InlineData("/page#fragment")]
    [InlineData("/page?query=1#fragment")]
    [InlineData("/companies/123")]
    [InlineData("/companies/123/employees")]
    [InlineData("/companies/123/employees?page=2&size=10")]
    [InlineData("/dashboard")]
    [InlineData("/")]
    [InlineData("/?returnUrl=something")]
    public void ValidateInternalPath_AcceptsValidApplicationRelativePaths(string validPath)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(validPath);
        Assert.Equal(validPath, result);
    }

    [Theory]
    [InlineData("//attacker.example")]
    [InlineData("//attacker.example/path")]
    [InlineData("///path")]
    public void ValidateInternalPath_RejectsSchemeRelativeUris(string schemRelativePath)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(schemRelativePath);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://attacker.example/path")]
    [InlineData("javascript://alert()")]
    [InlineData("data://something")]
    [InlineData("ftp://attacker.example")]
    public void ValidateInternalPath_RejectsAbsoluteUrisWithSchemes(string absoluteUri)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(absoluteUri);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("javascript:alert()")]
    [InlineData("data:text/html,<script>alert()</script>")]
    [InlineData("vbscript:msgbox")]
    public void ValidateInternalPath_RejectsDataAndScriptSchemes(string dataUri)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(dataUri);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("/\\attacker.example")]
    [InlineData("/\\\\server/path")]
    [InlineData("C:\\path\\to\\file")]
    public void ValidateInternalPath_RejectsPathsWithBackslashes(string pathWithBackslash)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(pathWithBackslash);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("%3a%2f%2f", "case-insensitive hex %3a%2f%2f")]
    [InlineData("%3A%2F%2F", "uppercase hex %3A%2F%2F")]
    [InlineData("%3a//", "mixed hex and literal %3a//")]
    [InlineData("%3A//", "mixed uppercase hex and literal %3A//")]
    public void ValidateInternalPath_RejectsEncodedSchemeVariants(string encodedScheme, string _)
    {
        var path = $"/path{encodedScheme}attacker.example";
        var result = ReturnUrlValidator.ValidateInternalPath(path);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateInternalPath_RejectsEmptyAndWhitespace(string? emptyValue)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(emptyValue);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("path/without/leading/slash")]
    [InlineData("relative/path")]
    [InlineData("page")]
    public void ValidateInternalPath_RejectsRelativePathsWithoutLeadingSlash(string relativePath)
    {
        var result = ReturnUrlValidator.ValidateInternalPath(relativePath);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateInternalPath_TrimsWhitespaceFromValidPath()
    {
        var result = ReturnUrlValidator.ValidateInternalPath("  /getting-started  ");
        Assert.Equal("/getting-started", result);
    }

    [Fact]
    public void ValidateInternalPath_PreservesQueryStringsAndFragments()
    {
        var path = "/companies/123?filter=active&sort=name#section";
        var result = ReturnUrlValidator.ValidateInternalPath(path);
        Assert.Equal(path, result);
    }

    [Fact]
    public void ValidateInternalPath_AllowsUnicodeAndSpecialCharactersInPath()
    {
        var result = ReturnUrlValidator.ValidateInternalPath("/search?q=Café&lang=en");
        Assert.NotNull(result);
        Assert.Equal("/search?q=Café&lang=en", result);
    }

    [Fact]
    public void ValidateInternalPath_RejectsCandidateDetailListUrlOpenRedirect()
    {
        // Real attack scenario: attacker links to:
        // /companies/{id}/candidates/new?returnUrl=//attacker.example
        // The component would use this returnUrl as the fallback for Close button
        var result = ReturnUrlValidator.ValidateInternalPath("//attacker.example");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateInternalPath_RejectsReviewCvBackTargetOpenRedirect()
    {
        // Real attack scenario: attacker links to:
        // /companies/{cid}/vacancies/{vid}/applications/{aid}/review-cv?returnUrl=https://attacker.example
        var result = ReturnUrlValidator.ValidateInternalPath("https://attacker.example");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateInternalPath_RejectsEmployeeListBackUrlOpenRedirect()
    {
        // Real attack scenario: attacker links to:
        // /companies/{id}/employees?returnUrl=javascript:alert('xss')
        var result = ReturnUrlValidator.ValidateInternalPath("javascript:alert('xss')");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateInternalPath_RejectsEncodedJavascriptUri()
    {
        // Double-encoded or partially-encoded attempt to bypass filter
        var result = ReturnUrlValidator.ValidateInternalPath("/path%3ajavascript:alert()");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateInternalPath_AllowsValidInternalNavigationWithComplexQuery()
    {
        // Real app scenario: return to a filtered, sorted, paginated list view
        var returnUrl = "/companies/abc-123/employees?page=3&filter=department%3DHR&sort=-salary#employee-grid";
        var result = ReturnUrlValidator.ValidateInternalPath(returnUrl);
        Assert.Equal(returnUrl, result);
    }
}
