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
        // With prerender disabled the page is blank until the interactive circuit connects — gate
        // on the authenticated shell first so the grid-render budget isn't partly consumed by
        // circuit establishment on a cold shared E2E app.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 30_000 });
    }

    public async Task ClickNewCandidateAsync()
    {
        // Same race as EmployeeListPage.ClickNewEmployeeAsync: SearchPageBase's "hr-add" toolbar
        // handler silently no-ops while the page's async permission check is still pending
        // (GetAddUrl() returns null, nothing navigates, no error). A Recruiter-only account
        // (candidate:view is Recruiter-gated) hits this most under headless + parallel load.
        // Wait for the button, then retry the click until the navigation actually commits.
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await button.ClickAsync();
            try
            {
                // WaitUntil=Commit, not the default Load: a Blazor interactive navigation may
                // never re-fire the target document's "load" event.
                await page.WaitForURLAsync("**/candidates/new**",
                    new()
                    {
                        Timeout = attempt < maxAttempts ? 3_000 : 30_000,
                        WaitUntil = WaitUntilState.Commit,
                    });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                // Permission check likely still pending when we clicked — try again.
            }
        }
    }

    /// <summary>
    /// Returns true if a row containing <paramref name="nameFragment"/> exists ANYWHERE in the
    /// grid, not just on the currently displayed page. CandidateList.razor has no search box and
    /// pages client-side at 20 rows (GridPageSettings PageSize="20") — this suite has accumulated
    /// enough "E2E ..." candidates created by other test classes (never cleaned up) that a seeded
    /// candidate can land past page 1. Same pagination-aware reasoning as
    /// PositionProfileListPage.HasPositionProfileAsync — paginate through the grid instead of
    /// assuming page 1 is exhaustive.
    /// </summary>
    public async Task<bool> HasCandidateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var matchOnCurrentPage = page.Locator(".e-rowcell").Filter(new() { HasText = nameFragment }).First;

        for (var guard = 0; guard < 25; guard++)
        {
            if (await matchOnCurrentPage.IsVisibleAsync())
                return true;

            var nextPage = page.Locator(".e-pagernextprevdiv.e-next:not(.e-disable), a.e-next:not(.e-disable)").First;
            if (!await nextPage.IsVisibleAsync())
                return false;

            await nextPage.ClickAsync();
            await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(200);
        }

        return false;
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
