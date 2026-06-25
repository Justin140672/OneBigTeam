using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class DashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/");
        // Wait for the task widget to finish loading: either a task item appears or the
        // "All caught up!" empty state renders. Both are absent during the loading spinner phase.
        await page.WaitForSelectorAsync(".task-widget-item, .widget-empty", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the titles of all task items currently shown in the My Tasks widget.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var items = await page.Locator(".task-widget-item .task-widget-title").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>
    /// Clicks the first task item whose title contains <paramref name="titleFragment"/>
    /// and waits for navigation to the task view page.
    /// </summary>
    public async Task ClickTaskAsync(string titleFragment)
    {
        var item = page.Locator(".task-widget-item")
            .Filter(new() { HasText = titleFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForURLAsync(new Regex("/tasks/"), new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the My Tasks widget shows the "All caught up!" empty state.</summary>
    public async Task<bool> IsTaskListEmptyAsync() =>
        await page.Locator(".widget-empty").IsVisibleAsync();
}
