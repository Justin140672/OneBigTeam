using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Timeline" tab (EmployeeTimelineTab.razor), a hand-rolled vertical
/// timeline shared between the HR-facing employee edit page (EmployeeEdit.razor) and the
/// self-service profile page (MyProfile.razor). Both host pages expose this tab under the same
/// "Timeline" tab name and the same data-testid hooks, so a single page object works for either,
/// as long as the caller has already navigated to/opened the right host page and tab strip.
///
/// Note: EmployeeTimelineTab's data fetch (EmployeeTimelineService.GetTimelineAsync) runs
/// server-side from the Blazor Server circuit — there is no browser-visible XHR/fetch for it to
/// intercept via Playwright's network APIs. Pagination assertions in this suite therefore
/// verify effects (rendered entry list changes, URL unchanged) rather than the outgoing HTTP
/// request shape.
/// </summary>
public sealed class EmployeeTimelineTab(IPage page)
{
    private ILocator TimelineList => page.Locator("[data-testid='employee-timeline']");
    private ILocator EntryCards => page.Locator("[data-testid='timeline-entry-card']");
    private ILocator LoadMoreButton => page.Locator("[data-testid='timeline-load-more']");
    private ILocator EmptyState => page.Locator(".hr-empty-state")
        .Filter(new() { HasText = "No timeline entries found." });

    public async Task OpenAsync()
    {
        var activityGroup = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true });
        var timelineTab = page.GetByRole(AriaRole.Tab, new() { Name = "Timeline" });

        try
        {
            await page.Locator(".employee-profile-groups > .e-tab-header")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
        }

        if (await activityGroup.CountAsync() > 0)
        {
            await activityGroup.ClickAsync();
            await page.Locator(".employee-profile-sections > .e-tab-header")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }

        await timelineTab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await timelineTab.ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='employee-timeline'], .hr-empty-state",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> IsTabVisibleAsync()
    {
        var activityGroup = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true });
        if (await activityGroup.CountAsync() > 0)
            await activityGroup.ClickAsync();

        return await page.GetByRole(AriaRole.Tab, new() { Name = "Timeline" }).IsVisibleAsync();
    }

    public Task<bool> IsEmptyStateVisibleAsync() => EmptyState.IsVisibleAsync();

    public Task<int> GetEntryCountAsync() => EntryCards.CountAsync();

    public async Task<IReadOnlyList<string>> GetEntryTextsAsync()
    {
        var texts = await EntryCards.AllTextContentsAsync();
        return texts.Select(t => t.Trim()).ToList();
    }

    public ILocator EntryCard(string textFragment) =>
        EntryCards.Filter(new() { HasText = textFragment });

    public Task<bool> EntryHasUpcomingBadgeAsync(string textFragment) =>
        EntryCard(textFragment).First.Locator("[data-testid='upcoming-timeline-badge']").IsVisibleAsync();

    public async Task<bool> EntryHasViewDetailsLinkAsync(string textFragment)
    {
        var card = EntryCard(textFragment).First;
        if (!await card.IsVisibleAsync()) return false;
        return await card.GetByRole(AriaRole.Button, new() { Name = "View details" }).IsVisibleAsync();
    }

    public Task ClickViewDetailsAsync(string textFragment) =>
        EntryCard(textFragment).First.GetByRole(AriaRole.Button, new() { Name = "View details" }).ClickAsync();

    public Task<bool> HasLoadMoreButtonAsync() => LoadMoreButton.IsVisibleAsync();

    public async Task ClickLoadMoreAsync(int previousCount)
    {
        await LoadMoreButton.ClickAsync();
        await page.WaitForFunctionAsync(
            "expectedMin => document.querySelectorAll(\"[data-testid='timeline-entry-card']\").length > expectedMin",
            previousCount,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
