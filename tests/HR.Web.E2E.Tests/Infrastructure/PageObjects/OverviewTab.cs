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
        if (!await dt.IsVisibleAsync()) return null;
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
}
