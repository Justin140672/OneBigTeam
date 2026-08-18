using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class HrInboxPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/hr/inbox");
        await page.WaitForSelectorAsync(".inbox-card, .inbox-empty", new() { Timeout = 20_000 });
    }

    public async Task<bool> IsEmptyAsync() =>
        await page.Locator(".inbox-empty").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var titleEls = await page.Locator(".inbox-card-title").AllAsync();
        var titles = new List<string>();
        foreach (var t in titleEls)
            titles.Add((await t.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>
    /// Clicks Claim on the first inbox card whose title contains <paramref name="titleFragment"/>
    /// and waits for the card to be removed from the DOM.
    /// </summary>
    public async Task ClaimAsync(string titleFragment)
    {
        var card = page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .First;

        // Some task titles are fixed/generic with no employee-name suffix (e.g. "Review
        // outstanding documents for employee exit"), so more than one stray/unclaimed card in the
        // shared inbox can match the same titleFragment. A plain ILocator re-resolves its query
        // every time it's awaited — so after clicking Claim on "the first match" and it detaches,
        // WaitForAsync(Detached) on that same *locator* re-queries and matches whatever OTHER
        // stray card with the same text is now first, which never detaches, hanging forever.
        // Pin down the actual DOM node about to be clicked via an ElementHandle instead, so the
        // detached-wait tracks that specific element regardless of how many siblings share its text.
        var cardHandle = await card.ElementHandleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Claim" }).ClickAsync();

        // Blazor removes the card from _items after a successful claim → DOM detach
        await cardHandle.WaitForElementStateAsync(ElementState.Hidden, new() { Timeout = 15_000 });
    }

    public async Task<bool> HasTaskAsync(string titleFragment) =>
        await page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .IsVisibleAsync();
}
