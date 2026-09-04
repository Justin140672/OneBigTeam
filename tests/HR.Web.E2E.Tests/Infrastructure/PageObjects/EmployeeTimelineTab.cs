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

    /// <summary>Opens the "Timeline" tab and waits for either the entry list or the empty state to render.</summary>
    public async Task OpenAsync()
    {
        // EmployeeEdit hosts Timeline under an "Activity" group tab; MyProfile has it flat in a
        // single strip. Select the group first when it's present.
        var activityGroup = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true });
        if (await activityGroup.CountAsync() > 0)
            await activityGroup.ClickAsync();

        await page.GetByRole(AriaRole.Tab, new() { Name = "Timeline" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='employee-timeline'], .hr-empty-state",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> IsTabVisibleAsync()
    {
        // EmployeeEdit nests Timeline under an "Activity" group; MyProfile is flat. Open the group
        // first when present so the section tab is actually in the DOM.
        var activityGroup = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true });
        if (await activityGroup.CountAsync() > 0)
            await activityGroup.ClickAsync();

        return await page.GetByRole(AriaRole.Tab, new() { Name = "Timeline" }).IsVisibleAsync();
    }

    public Task<bool> IsEmptyStateVisibleAsync() => EmptyState.IsVisibleAsync();

    public Task<int> GetEntryCountAsync() => EntryCards.CountAsync();

    /// <summary>
    /// Returns the trimmed text content of every rendered entry card, in DOM order — which is
    /// the same order the component renders them in (newest event date first, see
    /// GetEmployeeTimelineHandler's OrderByDescending(EventDate).ThenByDescending(CreatedDate)).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEntryTextsAsync()
    {
        var texts = await EntryCards.AllTextContentsAsync();
        return texts.Select(t => t.Trim()).ToList();
    }

    /// <summary>The entry card whose rendered text contains <paramref name="textFragment"/> (e.g. a unique note/title fragment).</summary>
    public ILocator EntryCard(string textFragment) =>
        EntryCards.Filter(new() { HasText = textFragment });

    /// <summary>True if the entry card matching <paramref name="textFragment"/> shows the "Upcoming" badge (future-dated event).</summary>
    public Task<bool> EntryHasUpcomingBadgeAsync(string textFragment) =>
        EntryCard(textFragment).First.Locator("[data-testid='upcoming-timeline-badge']").IsVisibleAsync();

    /// <summary>True if the entry card matching <paramref name="textFragment"/> shows a "View details" link.</summary>
    public async Task<bool> EntryHasViewDetailsLinkAsync(string textFragment)
    {
        var card = EntryCard(textFragment).First;
        if (!await card.IsVisibleAsync()) return false;
        return await card.GetByRole(AriaRole.Button, new() { Name = "View details" }).IsVisibleAsync();
    }

    /// <summary>Clicks "View details" on the entry card matching <paramref name="textFragment"/>, triggering a same-page tab switch.</summary>
    public Task ClickViewDetailsAsync(string textFragment) =>
        EntryCard(textFragment).First.GetByRole(AriaRole.Button, new() { Name = "View details" }).ClickAsync();

    public Task<bool> HasLoadMoreButtonAsync() => LoadMoreButton.IsVisibleAsync();

    /// <summary>
    /// Clicks "Load more" and waits for the entry count to increase beyond
    /// <paramref name="previousCount"/> — the signal that the server round-trip (over the
    /// existing SignalR circuit, not a fresh page load) has completed and appended more entries,
    /// without a full page reload.
    /// </summary>
    public async Task ClickLoadMoreAsync(int previousCount)
    {
        await LoadMoreButton.ClickAsync();
        await page.WaitForFunctionAsync(
            "expectedMin => document.querySelectorAll(\"[data-testid='timeline-entry-card']\").length > expectedMin",
            previousCount,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
