using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class ReportCatalogPage(IPage page, string baseUrl)
{
    private const string CardsRenderedSelector = ".report-catalog-card, .hr-empty-state";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting");
        await page.WaitForSelectorAsync(CardsRenderedSelector, new() { Timeout = 20_000 });
    }

    private ILocator Card(string nameFragment) =>
        page.Locator(".report-catalog-card").Filter(new() { HasText = nameFragment }).First;

    public async Task<bool> HasCardAsync(string nameFragment) =>
        await Card(nameFragment).IsVisibleAsync();

    public async Task<int> GetVisibleCardCountAsync() =>
        await page.Locator(".report-catalog-card").CountAsync();

    public async Task<string?> GetCardDescriptionAsync(string nameFragment)
    {
        var text = await Card(nameFragment).Locator(".card-text").TextContentAsync();
        return text?.Trim();
    }

    public async Task<bool> IsCardClickableAsync(string nameFragment) =>
        await Card(nameFragment).Locator("text=Coming soon").CountAsync() == 0;

    public async Task SearchAsync(string query)
    {
        var searchInput = page.GetByPlaceholder("Search reports by name or description");
        await searchInput.FillAsync(query);
        await searchInput.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public async Task ClickFavouriteAsync(string nameFragment)
    {
        var button = Card(nameFragment).Locator(".report-catalog-favourite");
        var wasActive = (await button.GetAttributeAsync("class"))?.Contains("report-catalog-favourite--active") == true;

        await button.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var nowActive = (await button.GetAttributeAsync("class"))?.Contains("report-catalog-favourite--active") == true;
            if (nowActive != wasActive) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for the favourite toggle on '{nameFragment}' to commit.");
            await page.WaitForTimeoutAsync(150);
        }
    }

    public async Task<bool> IsFavouritedAsync(string nameFragment)
    {
        var button = Card(nameFragment).Locator(".report-catalog-favourite");
        var cssClass = await button.GetAttributeAsync("class");
        return cssClass?.Contains("report-catalog-favourite--active") == true;
    }

    public async Task<IReadOnlyList<string>> GetCardTitlesInCategoryAsync(string categoryFragment)
    {
        var heading = page.Locator("h5").Filter(new() { HasText = categoryFragment }).First;
        var row = heading.Locator("xpath=following-sibling::div[contains(@class,'row')][1]");
        var titles = await row.Locator(".card-title").AllAsync();
        var result = new List<string>();
        foreach (var title in titles)
            result.Add((await title.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task ClickCardAsync(string nameFragment)
    {
        await Card(nameFragment).ClickAsync();
    }

    public ILocator CardLink(string nameFragment) =>
        Card(nameFragment).GetByRole(AriaRole.Link);

    public ILocator FavouriteButton(string nameFragment) =>
        Card(nameFragment).GetByRole(AriaRole.Button);

    public ILocator SearchBox => page.GetByRole(AriaRole.Textbox, new() { Name = "Search reports" });
}
