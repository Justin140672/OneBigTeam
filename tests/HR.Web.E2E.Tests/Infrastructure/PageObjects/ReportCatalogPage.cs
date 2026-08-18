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
        // The filtered re-render happens on the next Blazor render tick after the blur-triggered
        // ValueChanged callback — reading GetVisibleCardCountAsync() immediately after PressAsync
        // can race that and still see the pre-filter card count (same reasoning as
        // VacancyListPage.SearchAsync's post-search settle wait).
        await page.WaitForTimeoutAsync(300);
    }

    public async Task ClickFavouriteAsync(string nameFragment)
    {
        var button = Card(nameFragment).Locator(".report-catalog-favourite");
        var wasActive = (await button.GetAttributeAsync("class"))?.Contains("report-catalog-favourite--active") == true;

        await button.ClickAsync();

        // Favourites round-trip through the server (ReportingService's Add/RemoveReportFavouriteAsync,
        // not localStorage — see class remarks elsewhere in this suite), so the click dispatching is
        // not proof the toggle has actually committed yet. A caller that immediately re-reads
        // IsFavouritedAsync() right after (in particular the self-heal checks at the top of
        // FavouriteToggle_PersistsAcrossReload_AndSortsFirstInCategory /
        // FavouritingNewReportCard_PersistsAcrossNavigationAwayAndBack, which click-then-immediately-
        // assert to repair a possibly-already-polluted starting state) can otherwise race the
        // round-trip and see the pre-toggle state, making the self-heal itself silently a no-op
        // under load. Poll until the CSS class has actually flipped before returning.
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
