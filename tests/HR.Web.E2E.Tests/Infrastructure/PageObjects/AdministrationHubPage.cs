using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// ADM-04 — the Administration settings hub (Components/Pages/Administration/AdministrationHub.razor,
/// route "/companies/{CompanyId}/administration"). Category cards render a heading per category, a
/// list of links to existing settings screens, and a "Not yet configurable" badge on links that have
/// no destination yet.
/// </summary>
public sealed class AdministrationHubPage(IPage page, string baseUrl)
{
    public string RouteFor(Guid companyId) => $"{baseUrl}/companies/{companyId}/administration";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync(RouteFor(companyId));
        // The page either renders its heading, or (no visible category) client-side redirects to
        // /access-denied — wait for whichever settles.
        await page.WaitForSelectorAsync("h1:has-text(\"Administration\"), h1:has-text(\"Access denied\")",
            new() { Timeout = 20_000 });
    }

    public Task<bool> HasCategoryAsync(string title) =>
        page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true }).IsVisibleAsync();

    public Task<bool> HasNotYetConfigurableMarkerAsync() =>
        page.GetByText("Not yet configurable").First.IsVisibleAsync();

    public async Task ClickLinkAsync(string linkText)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = linkText, Exact = true }).First.ClickAsync();
    }

    public Task<bool> BreadcrumbHasAdministrationAsync() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Administration", Exact = true }).First.IsVisibleAsync();

    public async Task ClickAdministrationBreadcrumbAsync()
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Administration", Exact = true }).First.ClickAsync();
    }
}
