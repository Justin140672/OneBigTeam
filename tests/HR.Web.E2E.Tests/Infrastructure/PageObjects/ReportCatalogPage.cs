using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the report catalog landing page
/// (/companies/{companyId}/reporting — ReportCatalogPage.razor).
/// </summary>
public sealed class ReportCatalogPage(IPage page, string baseUrl)
{
    private const string CardsRenderedSelector = ".report-catalog-card, .hr-empty-state";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting");
        await page.WaitForSelectorAsync(CardsRenderedSelector, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the report catalog card whose title contains <paramref name="nameFragment"/>
    /// (e.g. "Employee Directory") — scoped narrowly to the card container rather than the
    /// bare ".report-catalog-card" class, since multiple cards share that class.
    /// </summary>
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
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change — an explicit blur is
        // needed for the search filter (client-side, computed off _searchTerm) to actually apply.
        await searchInput.PressAsync("Tab");
    }

    public async Task ClickFavouriteAsync(string nameFragment)
    {
        await Card(nameFragment).Locator(".report-catalog-favourite").ClickAsync();
    }

    public async Task<bool> IsFavouritedAsync(string nameFragment)
    {
        var button = Card(nameFragment).Locator(".report-catalog-favourite");
        var cssClass = await button.GetAttributeAsync("class");
        return cssClass?.Contains("report-catalog-favourite--active") == true;
    }

    /// <summary>
    /// Returns the trimmed titles of every card within the category section whose heading
    /// contains <paramref name="categoryFragment"/> (e.g. "Recruitment"/"Hr"), in DOM order —
    /// used to prove favourites sort first within their category.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCardTitlesInCategoryAsync(string categoryFragment)
    {
        var heading = page.Locator("h5").Filter(new() { HasText = categoryFragment }).First;
        // The category's card row is the next sibling ".row" element after its <h5> heading.
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
}
