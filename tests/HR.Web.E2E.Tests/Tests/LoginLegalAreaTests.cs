using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Story 3: the compact legal / trust area beneath the login form links out to the marketing
/// site's policy pages. Verifies all six expected links render, that every href is an absolute
/// URL (marketing may be on a different domain) and that the policy paths are present.
/// Uses <see cref="ParallelBlankPersonaFixture"/> so the unauthenticated login form renders.
/// </summary>
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

        // The "return to website" link points at the marketing base URL root (no policy path).
        Assert.Contains(links, l => l.Text.Contains("Return to", StringComparison.OrdinalIgnoreCase));
    }
}
