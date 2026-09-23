using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for <c>InviteModeCandidateGrid.razor</c> — the candidate grid rendered on the
/// Employee List page when navigated in invitation mode (<c>?mode=invite</c>). Auto-selects every
/// candidate with a non-empty work email on load; candidates missing a work email are shown but
/// never auto-selected, with an inline explanation + edit link instead.
/// </summary>
public sealed class InviteModeCandidateGridPage(IPage page)
{
    private ILocator Root => page.Locator(".invite-mode-candidate-grid");

    private const string RowsRenderedSelector =
        ".invite-mode-grid .e-row, .invite-mode-grid .e-emptyrow";

    /// <summary>
    /// Waits for the candidate grid to finish loading (its own async fetch,
    /// UserAdministrationService.GetInvitableEmployeesAsync). Preselection is computed on the data
    /// before the grid first renders (InviteModeCandidateGrid owns selection by EmployeeId), so the
    /// "N selected" summary is already correct by the time rows exist; the poll below just guards
    /// the first render after the loading indicator is swapped out.
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await GetSelectedCountAsync() == 0 && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    private ILocator RowOnCurrentPage(string nameFragment) =>
        page.Locator(".invite-mode-grid .e-row").Filter(new() { HasText = nameFragment }).First;

    private ILocator FirstRow => page.Locator(".invite-mode-grid .e-row").First;

    /// <summary>
    /// Finds the row matching <paramref name="nameFragment"/> ANYWHERE in the grid: checks the
    /// current page, then rewinds to page 1 and pages forward. This grid has no search box and a
    /// 20-row PageSize, and the seeded E2E pool alone is 60+ Acme employees, so a candidate can sit
    /// on any page — including one BEFORE the page an earlier lookup left the grid on.
    /// </summary>
    private async Task<ILocator> Row(string nameFragment)
    {
        var row = RowOnCurrentPage(nameFragment);
        if (await row.CountAsync() > 0)
            return row;

        await ClickPagerAndWaitAsync(page.Locator(".invite-mode-grid .e-pager .e-first:not(.e-disable)"));

        for (var pageIndex = 1; pageIndex <= 50; pageIndex++)
        {
            if (await row.CountAsync() > 0)
                return row;

            if (!await ClickPagerAndWaitAsync(page.Locator(".invite-mode-grid .e-pager .e-next:not(.e-disable)")))
                break; // No more pages.
        }

        // Not found anywhere — return the locator so the caller's own wait/assert produces the
        // usual "not found" failure rather than a null-reference surprise.
        return row;
    }

    /// <summary>
    /// Clicks a pager control (if present/enabled) and waits for the page swap to actually land:
    /// the old page's rows are still in the DOM right after the click, so a plain "rows rendered"
    /// wait would return immediately and the caller would search stale rows.
    /// </summary>
    private async Task<bool> ClickPagerAndWaitAsync(ILocator pagerControl)
    {
        if (await pagerControl.CountAsync() == 0)
            return false;

        var firstRowBefore = await FirstRow.InnerTextAsync();
        await pagerControl.First.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
               (await FirstRow.CountAsync() == 0 || await FirstRow.InnerTextAsync() == firstRowBefore))
        {
            await page.WaitForTimeoutAsync(100);
        }

        return true;
    }

    public async Task<bool> IsRowCheckedAsync(string nameFragment)
    {
        var row = await Row(nameFragment);
        var input = row.Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        return await input.IsCheckedAsync();
    }

    /// <summary>
    /// Toggles the checkbox for the row matching <paramref name="nameFragment"/> (checks it if
    /// unchecked, unchecks if checked — same click target either direction), then waits for the
    /// server-rendered "N selected" summary to reflect it. The native checkbox flips on click
    /// immediately, but the component's selection state (and the summary/Invite button) only
    /// update after the Blazor Server round-trip.
    /// </summary>
    public async Task ToggleRowAsync(string nameFragment)
    {
        var row = await Row(nameFragment);
        var input = row.Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        var wasChecked = await input.IsCheckedAsync();
        var countBefore = await GetSelectedCountAsync();
        var expectedCount = wasChecked ? countBefore - 1 : countBefore + 1;

        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();

        await WaitForSelectedCountAsync(count => count == expectedCount);
    }

    /// <summary>
    /// True if the row matching <paramref name="nameFragment"/> shows the "No work email —
    /// correct this employee's record" inline explanation (InviteModeCandidateGrid's Work Email
    /// column template), rather than a plain email value.
    /// </summary>
    public async Task<bool> ShowsMissingEmailExplanationAsync(string nameFragment) =>
        await (await Row(nameFragment)).GetByText("No work email", new() { Exact = false }).IsVisibleAsync();

    /// <summary>
    /// Href of the "correct this employee's record" edit link inside the missing-email
    /// explanation for the row matching <paramref name="nameFragment"/>.
    /// </summary>
    public async Task<string?> GetMissingEmailEditLinkHrefAsync(string nameFragment) =>
        await (await Row(nameFragment)).GetByRole(AriaRole.Link).Last.GetAttributeAsync("href");

    /// <summary>Reads the "N selected" summary text next to the toolbar buttons.</summary>
    public async Task<int> GetSelectedCountAsync()
    {
        var text = (await Root.Locator("span.text-muted").First.InnerTextAsync()).Trim();
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public ILocator SelectAllEligibleButton => Root.GetByRole(AriaRole.Button, new() { Name = "Select all eligible" });
    public ILocator ClearSelectionButton => Root.GetByRole(AriaRole.Button, new() { Name = "Clear selection" });

    public async Task ClickSelectAllEligibleAsync()
    {
        await SelectAllEligibleButton.ClickAsync();
        await WaitForSelectedCountAsync(count => count > 0);
    }

    public async Task ClickClearSelectionAsync()
    {
        await ClearSelectionButton.ClickAsync();
        await WaitForSelectedCountAsync(count => count == 0);
    }

    // The summary is re-rendered from the component's own selection state after the click's
    // server round-trip — wait for it rather than sleeping a fixed interval.
    private async Task WaitForSelectedCountAsync(Func<int, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition(await GetSelectedCountAsync()) && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    /// <summary>The grid's own "Invite selected (N)" primary action button.</summary>
    public ILocator InviteSelectedButton => Root.Locator("#invite-mode-invite-selected-btn");

    public Task<bool> IsInviteSelectedButtonDisabledAsync() => InviteSelectedButton.IsDisabledAsync();

    public Task ClickInviteSelectedAsync() => InviteSelectedButton.ClickAsync();
}
