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

        await card.GetByRole(AriaRole.Button, new() { Name = "Claim" }).ClickAsync();

        // Blazor removes the card from _items after a successful claim → DOM detach
        await card.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }

    public async Task<bool> HasTaskAsync(string titleFragment) =>
        await page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .IsVisibleAsync();
}
