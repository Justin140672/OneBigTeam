using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LoginLegalAreaTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private static readonly string[] ExpectedPaths =
    [
        "/privacy-policy",
        "/acceptable-use-policy",
        "/cookie-policy",
        "/terms-of-service",
        "/security",
    ];

    [Fact]
    public async Task LoginPage_LegalArea_RendersSixAbsolutePolicyLinks()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();

        var links = await login.GetLegalLinksAsync();

        Assert.Equal(6, links.Count);

        foreach (var (text, href) in links)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(
                Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                $"Legal link '{text}' href is not an absolute http(s) URL: '{href}'");
        }

        foreach (var path in ExpectedPaths)
        {
            Assert.Contains(links, l => l.Href.Contains(path, StringComparison.Ordinal));
        }

        Assert.Contains(links, l => l.Text.Contains("Return to", StringComparison.OrdinalIgnoreCase));
    }
}
