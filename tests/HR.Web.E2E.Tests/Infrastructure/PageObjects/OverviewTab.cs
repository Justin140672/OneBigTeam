using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Overview tab on the self-service My Profile page.
/// </summary>
public sealed class OverviewTab(IPage page)
{
    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(".overview-grid, .alert", new() { Timeout = 15_000 });
        // Wait for any skeleton loading state to disappear.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.overview-skeleton')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".overview-grid").IsVisibleAsync();

    /// <summary>Returns the text content of the dd element that follows a dt matching <paramref name="label"/>.</summary>
    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator(".overview-dl dt").Filter(new() { HasText = label }).First;
        // WaitForLoadAsync only proves the overview grid container/skeleton state has settled,
        // not that this specific dt/dd pair has rendered yet — a bare instant IsVisibleAsync()
        // right after can still race it (same class of race fixed elsewhere in this suite).
        try
        {
            await dt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }

    public async Task ClickRequestLeaveAsync()
    {
        await page.Locator(".action-btn").Filter(new() { HasText = "Request Leave" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-dialog", new() { Timeout = 10_000 });
    }

    public async Task ClickNotifySicknessAsync()
    {
        await page.Locator(".action-btn").Filter(new() { HasText = "Notify Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    public async Task ClickViewDocumentsAsync() =>
        await page.Locator(".action-btn").Filter(new() { HasText = "View Documents" }).ClickAsync();

    public async Task ClickViewTasksAsync() =>
        await page.Locator(".action-btn").Filter(new() { HasText = "View Tasks" }).ClickAsync();

    /// <summary>
    /// Returns the dt label texts, in DOM order, for the Employment summary card
    /// (the first overview card, headed "Employment").
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEmploymentCardLabelsAsync()
    {
        var card = page.Locator(".overview-card").Filter(new() { HasText = "Employment" }).First;
        var dts = await card.Locator("dt").AllAsync();
        var labels = new List<string>();
        foreach (var dt in dts)
            labels.Add((await dt.TextContentAsync())?.Trim() ?? "");
        return labels;
    }

    /// <summary>Returns all stat card titles currently rendered in the stats row.</summary>
    public async Task<IReadOnlyList<string>> GetStatCardTitlesAsync()
    {
        var cards = await page.Locator(".stat-card").AllAsync();
        var titles = new List<string>();
        foreach (var card in cards)
            titles.Add((await card.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>
    /// Returns the numeric value (".stat-value") of the stats-row card whose label
    /// (".stat-label") matches <paramref name="label"/> exactly (e.g. "Open Tasks").
    /// </summary>
    public async Task<string?> GetStatValueAsync(string label)
    {
        var card = page.Locator(".stat-card").Filter(new() { HasText = label }).First;
        try
        {
            await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await card.Locator(".stat-value").TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Clicks the stats-row card whose label (".stat-label") matches <paramref name="label"/>
    /// exactly (e.g. "Open Tasks") — each card now navigates to its relevant My Profile tab
    /// (MyProfileOverviewTab.razor's OnNavigateToTasks/OnNavigateToLeave/OnNavigateToSickness).
    /// </summary>
    public async Task ClickStatCardAsync(string label)
    {
        await page.Locator(".stat-card").Filter(new() { HasText = label }).First.ClickAsync();

        // Clicking a stat card switches the active tab, but the tab-switch is driven by a Blazor
        // re-render, not a full navigation — the previously-active "Overview" tab element can
        // still report aria-selected="true" for a moment after the click returns. Give the
        // selection change a moment to land before the caller reads the now-active tab, otherwise
        // MyProfilePage.GetActiveTabNameAsync races and returns "Overview" for every stat card.
        try
        {
            await page.WaitForFunctionAsync(
                "() => { const el = document.querySelector('[role=\"tab\"][aria-selected=\"true\"]'); return el && el.textContent.trim() !== 'Overview'; }",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // Fall through — the caller's own assertion on the resulting tab name will surface
            // the mismatch with a clearer message than swallowing it here would.
        }
    }

    /// <summary>
    /// Returns true if the "Onboarding Progress" card is rendered — only present when the
    /// current employee has an in-progress onboarding plan (Session.EmployeeId's
    /// MyOnboardingStatusModel.HasPlan is true). See MyProfileOverviewTab.razor.
    /// </summary>
    public Task<bool> HasOnboardingProgressCardAsync() =>
        page.Locator(".overview-card-title").Filter(new() { HasText = "Onboarding Progress" }).IsVisibleAsync();
}
