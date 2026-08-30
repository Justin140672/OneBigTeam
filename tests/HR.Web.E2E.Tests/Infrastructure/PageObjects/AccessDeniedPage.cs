using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// The shared "/access-denied" page (Components/Pages/AccessDenied.razor) that every admin-page
/// capability guard redirects to (replace) when the current persona lacks the required
/// permission-derived capability flag on AppSession.
/// </summary>
public sealed class AccessDeniedPage(IPage page, string baseUrl)
{
    public const string Route = "/access-denied";

    public bool IsOnRoute => page.Url.Split('?')[0].TrimEnd('/').EndsWith(Route, StringComparison.OrdinalIgnoreCase);

    public ILocator Heading => page.GetByRole(AriaRole.Heading, new() { Name = "Access denied" });

    public ILocator GoHomeLink => page.GetByRole(AriaRole.Link, new() { Name = "Go to home" });

    public ILocator MyProfileLink => page.GetByRole(AriaRole.Link, new() { Name = "My Profile" });

    public async Task WaitForLoadedAsync(int timeoutMs = 15_000)
    {
        await page.WaitForURLAsync(u => u.Split('?')[0].TrimEnd('/').EndsWith(Route, StringComparison.OrdinalIgnoreCase),
            new() { Timeout = timeoutMs });
        await Heading.WaitForAsync(new() { Timeout = timeoutMs });
    }
}
