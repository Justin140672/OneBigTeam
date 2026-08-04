using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Explore One Big Team" page (/explore — Explore.razor), gated to HR
/// Administrator / Company Administrator the same way as GettingStartedPage.
/// </summary>
public sealed class ExploreCardsPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".row.g-3, .alert-danger";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/explore");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Cards are rendered as bare ".card" (ExploreCard.razor) with no distinguishing class of
    /// their own, so every lookup here is scoped by the card's Name text via Filter — the same
    /// convention used by GettingStartedPage.TaskCard and ReportCatalogPage.Card.
    /// </summary>
    private ILocator Card(string nameFragment) =>
        page.Locator(".card").Filter(new() { HasText = nameFragment }).First;

    public Task<bool> HasCardAsync(string nameFragment) =>
        Card(nameFragment).IsVisibleAsync();

    public Task<int> GetCardCountAsync() =>
        page.Locator(".row.g-3 > .col-md-4 > .card").CountAsync();

    /// <summary>
    /// True when the card shows the "Coming Soon" badge/disabled button (ExploreCard.razor's
    /// IsComingSoon branch — currently only the Reports card).
    /// </summary>
    public Task<bool> IsComingSoonAsync(string nameFragment) =>
        Card(nameFragment).GetByText("Coming Soon").First.IsVisibleAsync();

    public async Task<bool> IsCardClickableAsync(string nameFragment) =>
        await Card(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Explore" }).IsVisibleAsync();

    public async Task<string?> GetCardLinkUrlAsync(string nameFragment)
    {
        var link = Card(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Explore" });
        return await link.IsVisibleAsync() ? await link.GetAttributeAsync("href") : null;
    }

    public Task ClickCardAsync(string nameFragment) =>
        Card(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Explore" }).ClickAsync();
}
