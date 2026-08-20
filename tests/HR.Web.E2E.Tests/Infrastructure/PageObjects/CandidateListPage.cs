using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the candidate list page (/companies/{companyId}/candidates).
/// </summary>
public sealed class CandidateListPage(IPage page, string baseUrl)
{
    // ".e-grid" alone doesn't prove rows are queryable — Syncfusion's EJ2 grid populates
    // ".e-row"/".e-rowcell" on its own JS render tick after the Blazor component mounts, so the
    // row selector (or its empty-state sibling) is the only wait actually tied to data being
    // present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewCandidateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/candidates/new", new() { Timeout = 30_000 });
    }

    public async Task<bool> HasCandidateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public async Task ClickCandidateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/candidates/**", new() { Timeout = 15_000 });
    }

    // ── Active/Inactive filter + status badge (Status column, ActiveStatusBadge) ────────────────

    /// <summary>
    /// Toggles the "Show Inactive"/"Show Active" toolbar button, following the same
    /// SupportsActiveFilter toolbar convention as ExternalRecruiterListPage.ShowInactiveAsync.
    /// </summary>
    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ShowActiveOnlyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Active" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    private ILocator Row(string nameFragment) =>
        page.Locator(".e-row").Filter(new() { HasText = nameFragment }).First;

    /// <summary>
    /// True if the given candidate's row is present and its Status column shows the "Active"
    /// badge (ActiveStatusBadge -> StatusBadge with the "bg-success" variant class).
    /// </summary>
    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await Row(nameFragment).Locator(".status-badge.status-badge--success").IsVisibleAsync();
    }
}
