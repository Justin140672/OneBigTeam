using HR.Modules.Support.Domain;
using HR.SharedKernel.Html;

namespace HR.Modules.Support.Tests;

public class SupportResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    internal const string MaliciousBody =
        "<p>Hi <strong>there</strong></p><script>alert(1)</script><img src=x onerror=alert(1)>" +
        "<a href=\"javascript:alert(1)\">x</a><iframe src=\"https://evil.example\"></iframe>";

    private static SupportResponse Create(string body) =>
        SupportResponse.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, body, Now);

    internal static void OverwriteBodyWithRawLegacyValue(SupportResponse response, string rawBody) =>
        typeof(SupportResponse).GetProperty(nameof(SupportResponse.BodyHtml))!.SetValue(response, rawBody);

    internal static void AssertBodyIsClean(string body)
    {
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SupportHtmlSanitizer.Sanitize(body), body);
    }

    [Fact]
    public void Create_Sanitises_Body()
    {
        var response = Create(MaliciousBody);

        AssertBodyIsClean(response.BodyHtml);
        Assert.Contains("<strong>there</strong>", response.BodyHtml);
        Assert.Equal(SupportHtmlSanitizer.Sanitize(MaliciousBody), response.BodyHtml);
    }

    [Fact]
    public void Create_Keeps_Plain_Text_Body_Unchanged()
    {
        const string plain = "Thanks, that fixed it.";

        Assert.Equal(plain, Create(plain).BodyHtml);
    }

    [Fact]
    public void Create_Stores_Empty_Body_When_Nothing_Permitted_Remains()
    {
        var response = Create("<img src=x onerror=alert(1)><iframe src=\"javascript:alert(1)\"></iframe>");

        Assert.Equal(string.Empty, response.BodyHtml);
    }

    [Fact]
    public void ResanitiseBody_Returns_False_And_Changes_Nothing_For_A_Clean_Body()
    {
        var response = Create("<p>Hello <strong>team</strong> <a href=\"https://example.com/help\">docs</a></p>");
        var before = response.BodyHtml;

        Assert.False(response.ResanitiseBody());
        Assert.Equal(before, response.BodyHtml);
    }

    [Fact]
    public void ResanitiseBody_Returns_False_For_An_Empty_Body()
    {
        var response = Create(string.Empty);

        Assert.False(response.ResanitiseBody());
        Assert.Equal(string.Empty, response.BodyHtml);
    }

    [Fact]
    public void ResanitiseBody_Cleans_A_Legacy_Raw_Body_Then_Is_A_NoOp_On_Repeat()
    {
        var response = Create("placeholder");
        OverwriteBodyWithRawLegacyValue(response, MaliciousBody);
        Assert.Equal(MaliciousBody, response.BodyHtml);

        Assert.True(response.ResanitiseBody());
        AssertBodyIsClean(response.BodyHtml);
        Assert.Contains("<strong>there</strong>", response.BodyHtml);
        var afterFirst = response.BodyHtml;

        Assert.False(response.ResanitiseBody());
        Assert.Equal(afterFirst, response.BodyHtml);
    }

    [Fact]
    public void ResanitiseBody_Returns_True_When_Only_Surrounding_Whitespace_Differs()
    {
        var response = Create("placeholder");
        OverwriteBodyWithRawLegacyValue(response, "  <p>ok</p>  ");

        Assert.True(response.ResanitiseBody());
        Assert.Equal("<p>ok</p>", response.BodyHtml);
    }
}
