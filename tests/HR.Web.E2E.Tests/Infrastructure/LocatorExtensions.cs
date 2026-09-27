using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class LocatorExtensions
{
    /// <summary>
    /// Non-throwing "is this visible" check that actually waits, unlike <see cref="ILocator.IsVisibleAsync"/>
    /// (which takes an instantaneous DOM snapshot with no auto-wait). Used for dialogs/elements whose
    /// appearance depends on a just-triggered client or server round trip (e.g. a Blazor Server
    /// unsaved-changes confirm dialog) — checking immediately after the triggering click is a race
    /// that reads "not visible yet" as "will never be visible", which is what made every one of these
    /// checks across the page-object suite flaky under headless/loaded runs.
    /// </summary>
    public static async Task<bool> WaitUntilVisibleAsync(this ILocator locator, int timeoutMs = 5_000)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// True if a grid cell containing <paramref name="text"/> exists on ANY page of the (single)
    /// Syncfusion grid on the page — not just the page currently displayed. List pages bind the full
    /// result set to a client-paged grid (PageSize=20) with no search box, and every E2E run keeps
    /// adding "E2E ..." rows that sort ahead of or alongside the one a test just created, so "is it
    /// on page 1" is order- and history-dependent. Checks the current page first (with a short
    /// render wait), then rewinds to page 1 and pages forward, waiting for each page swap to land
    /// (the old page's rows are still in the DOM right after the pager click) before re-checking.
    /// </summary>
    public static async Task<bool> HasGridCellOnAnyPageAsync(this IPage page, string text)
    {
        var match = page.Locator(".e-grid .e-rowcell").Filter(new() { HasText = text }).First;
        if (await match.WaitUntilVisibleAsync(5_000))
            return true;

        await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-first:not(.e-disable)");

        for (var guard = 0; guard < 50; guard++)
        {
            if (await match.IsVisibleAsync())
                return true;

            if (!await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-next:not(.e-disable)"))
                return false;
        }

        return false;
    }

    /// <summary>
    /// Clicks a list page's grid-toolbar "Add" button and waits for the create route. The toolbar's
    /// click handling is wired on a separate render pass AFTER the grid rows first paint, so a click
    /// fired right after a list page's readiness wait can be silently dropped (documented on
    /// ExternalRecruiterListPage.ClickNewAsync). Re-clicks only while the URL provably hasn't
    /// changed — the navigation itself happens at most once — and on final failure reports where
    /// the page actually is.
    /// </summary>
    public static async Task ClickGridAddAndWaitForCreateRouteAsync(this IPage page, string createUrlGlob)
    {
        var addButton = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await addButton.ClickAsync();
            try
            {
                await page.WaitForURLAsync(createUrlGlob, new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
                return;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                // Toolbar handler not attached yet when clicked — the URL is unchanged, click again.
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Clicking 'Add' did not navigate to '{createUrlGlob}' after 3 attempts (page is at {page.Url}).", ex);
            }
        }
    }

    /// <summary>
    /// Rewinds the page's (single) Syncfusion grid to page 1, then calls <paramref name="visitPage"/>
    /// once per page in pager order until it returns true (done), the last page has been visited,
    /// or <paramref name="budget"/> elapses. Each page swap is confirmed to have actually landed
    /// (first row's text changed) before the next visit, so a visit never re-reads a stale page or
    /// skips one — unlike a fixed sleep after clicking "next".
    /// </summary>
    public static async Task VisitAllGridPagesAsync(this IPage page, Func<Task<bool>> visitPage, TimeSpan budget)
    {
        await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-first:not(.e-disable)");

        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            if (await visitPage())
                return;

            if (DateTime.UtcNow >= deadline)
                return;

            if (!await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-next:not(.e-disable)"))
                return;
        }
    }

    private static async Task<bool> ClickGridPagerAndWaitAsync(IPage page, string pagerSelector)
    {
        var control = page.Locator(pagerSelector).First;
        if (await control.CountAsync() == 0)
            return false;

        var firstRow = page.Locator(".e-grid .e-row").First;
        var before = await firstRow.CountAsync() > 0 ? await firstRow.InnerTextAsync() : "";
        await control.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
               (await firstRow.CountAsync() == 0 || await firstRow.InnerTextAsync() == before))
        {
            await page.WaitForTimeoutAsync(100);
        }

        return true;
    }

    /// <summary>
    /// Waits out a Blazor Server "busy" round trip after clicking a save/confirm/deactivate button,
    /// without the classic race of checking "spinner gone" as the only condition. A bare
    /// WaitForFunctionAsync("!spinner || !visible") can resolve the instant it's called if the
    /// spinner's own render patch hasn't reached the browser yet over SignalR — i.e. it reads
    /// "hasn't started" as "already finished", and callers move on to assert new state before the
    /// server has actually applied it. Waiting for the spinner to appear first (tolerating it never
    /// showing, for round trips fast enough to skip a visible frame) then waiting for it to clear
    /// closes that gap.
    /// </summary>
    public static async Task WaitForSpinnerToClearAsync(this IPage page, int appearTimeoutMs = 2_000, int clearTimeoutMs = 15_000)
    {
        try
        {
            await page.Locator(".spinner-border").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = appearTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Round trip completed before a spinner frame ever rendered — nothing to wait out.
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = clearTimeoutMs });
    }
}
