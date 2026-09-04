using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// The shared "/access-denied" page (Components/Pages/AccessDenied.razor) that every admin-page
/// capability guard redirects to (replace) when the current persona lacks the required
/// permission-derived capability flag on AppSession.
/// </summary>
// CS9113 (baseUrl unread): kept for constructor-signature consistency with the other ~90 page
// objects in this suite, all of which take (IPage page, string baseUrl) even where — as here —
// the page has no direct-navigation helper yet. Removing it would make this one page object's
// constructor diverge from the established convention for a cosmetic gain.
#pragma warning disable CS9113
public sealed class AccessDeniedPage(IPage page, string baseUrl)
#pragma warning restore CS9113
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
