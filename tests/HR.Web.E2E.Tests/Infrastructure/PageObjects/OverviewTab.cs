using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class OverviewTab(IPage page)
{
    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(".overview-grid, .alert", new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.overview-skeleton')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".overview-grid").IsVisibleAsync();

    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator(".overview-dl dt").Filter(new() { HasText = label }).First;
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

    public async Task<IReadOnlyList<string>> GetEmploymentCardLabelsAsync()
    {
        var card = page.Locator(".overview-card").Filter(new() { HasText = "Employment" }).First;
        var dts = await card.Locator("dt").AllAsync();
        var labels = new List<string>();
        foreach (var dt in dts)
            labels.Add((await dt.TextContentAsync())?.Trim() ?? "");
        return labels;
    }

    public async Task<IReadOnlyList<string>> GetStatCardTitlesAsync()
    {
        var cards = await page.Locator(".stat-card").AllAsync();
        var titles = new List<string>();
        foreach (var card in cards)
            titles.Add((await card.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

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

    public async Task ClickStatCardAsync(string label)
    {
        await page.Locator(".stat-card").Filter(new() { HasText = label }).First.ClickAsync();

        try
        {
            await page.WaitForFunctionAsync(
                "() => { const el = document.querySelector('[role=\"tab\"][aria-selected=\"true\"]'); return el && el.textContent.trim() !== 'Overview'; }",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    public Task<bool> HasOnboardingProgressCardAsync() =>
        page.Locator(".overview-card-title").Filter(new() { HasText = "Onboarding Progress" }).IsVisibleAsync();
}
