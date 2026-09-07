using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the position profile list page (/companies/{companyId}/position-profiles).
/// </summary>
public sealed class PositionProfileListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles");
        // With prerender disabled the page is blank until the interactive circuit connects — gate
        // on the authenticated shell first so the render budget isn't partly consumed by circuit
        // establishment on a cold shared E2E app.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 30_000 });
        await page.WaitForSpinnerToClearAsync();
        // Don't return while the grid is still mounting — the toolbar "Add" button the callers
        // click next only renders once the grid component itself has rendered.
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger",
            new() { Timeout = 30_000 });
    }

    public async Task ClickNewPositionProfileAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        // WaitUntil=Commit, not the default Load: a Blazor interactive navigation may never
        // re-fire the target document's "load" event.
        await page.WaitForURLAsync("**/position-profiles/new**",
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
    }

    /// <summary>
    /// Returns true if a row containing <paramref name="titleFragment"/> exists ANYWHERE in the
    /// grid, not just on the currently displayed page. The grid pages client-side at 20 rows
    /// (GridPageSettings PageSize="20" in PositionProfileList.razor) over the full, alphabetically
    /// title-sorted result set (ListPositionProfiles orders by Title) — this suite has accumulated
    /// enough "E2E ..."-titled profiles created by other test classes (never cleaned up) that a
    /// seeded profile sorting after them (e.g. "Software Engineer") can now land past page 1. A
    /// bare current-page-only check here is a real, growing flakiness source as the suite grows,
    /// not a one-off data collision — paginate through the grid instead of assuming page 1 is
    /// exhaustive.
    /// </summary>
    public async Task<bool> HasPositionProfileAsync(string titleFragment)
    {
        var matchOnCurrentPage = page.Locator(".e-rowcell").Filter(new() { HasText = titleFragment }).First;

        for (var guard = 0; guard < 25; guard++)
        {
            if (await matchOnCurrentPage.IsVisibleAsync())
                return true;

            var nextPage = page.Locator(".e-pagernextprevdiv.e-next:not(.e-disable), a.e-next:not(.e-disable)").First;
            if (!await nextPage.IsVisibleAsync())
                return false;

            await nextPage.ClickAsync();
            await page.WaitForSpinnerToClearAsync();
            await page.WaitForTimeoutAsync(200);
        }

        return false;
    }

    public async Task<IReadOnlyList<string>> GetPositionProfileTitlesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var titles = new List<string>();
        foreach (var cell in cells)
            titles.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    public async Task OpenPositionProfileAsync(string title)
    {
        await page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.ClickAsync();
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Deactivates the position profile whose row contains <paramref name="title"/>
    /// by clicking the deactivate toolbar action.
    /// </summary>
    public async Task DeactivateAsync(string title)
    {
        // Select the row first, then click the deactivate toolbar button.
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        await row.ClickAsync();
        // Blazor re-renders the toolbar after row selection; wait for the button to be enabled
        // (same pattern as DepartmentListPage.DeactivateDepartmentAsync).
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        // Opens a confirmation dialog (HrConfirmDialog) rather than deactivating immediately —
        // scoped to the dialog since its own confirm button shares the "Deactivate" label with
        // the toolbar button just clicked above.
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        // Wait for the grid to refresh.
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string title)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        var badge = row.Locator(".status-badge.status-badge--success");
        return await badge.IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }
}
