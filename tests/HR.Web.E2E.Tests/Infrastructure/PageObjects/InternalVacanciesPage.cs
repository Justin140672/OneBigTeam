using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the self-service Internal Vacancies list
/// (src/HR.Web/Components/Pages/Recruitment/InternalVacancies.razor),
/// route /companies/{companyId}/internal-vacancies. Any authenticated employee of the company can
/// view it — no recruitment permission required. A vacancy appears here iff it belongs to the same
/// company AND Status == Open AND IsAdvertisedInternally == true.
/// </summary>
public sealed class InternalVacanciesPage(IPage page, string baseUrl)
{
    private const string CardSelector = "[data-testid='internal-vacancy-card']";
    private const string DetailSelector = "[data-testid='internal-vacancy-detail']";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/internal-vacancies");
        // With prerender disabled the page is blank until the interactive circuit connects — gate
        // on the authenticated shell first, then let WaitForInteractiveAsync settle the list.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits until the page's own heading has rendered and the list has settled onto either a
    /// populated card grid or the empty state (i.e. the "Loading vacancies…" indicator has gone).
    /// </summary>
    public async Task WaitForInteractiveAsync()
    {
        await page.GetByRole(AriaRole.Heading, new() { Name = "Internal Vacancies" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 30_000 });
    }

    /// <summary>Reads the page's main heading text.</summary>
    public async Task<string?> GetHeadingAsync()
    {
        var h1 = page.Locator("h1").First;
        await h1.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return (await h1.TextContentAsync())?.Trim();
    }

    public Task<int> CardCountAsync() => page.Locator(CardSelector).CountAsync();

    public Task<bool> HasCardAsync(string title) =>
        page.Locator(CardSelector).Filter(new() { HasText = title }).First.WaitUntilVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".vacancy-empty").WaitUntilVisibleAsync();

    /// <summary>
    /// Types <paramref name="query"/> into the optional search box and waits for the list to
    /// re-render. The SfTextBox raises ValueChange on blur/change (not on the "input" event
    /// FillAsync dispatches), so an explicit blur is needed to actually trigger OnSearchChanged.
    /// </summary>
    public async Task SearchAsync(string query)
    {
        // SfTextBox splats data-testid onto its underlying <input> directly (no wrapper), so match
        // the input itself; keep the descendant form as a fallback.
        var search = page.Locator(
            "input[data-testid='internal-vacancy-search'], [data-testid='internal-vacancy-search'] input").First;
        await search.FillAsync(query);
        await search.PressAsync("Tab");
        // Give the server round-trip that reloads _items a moment, then wait for the list to
        // settle again on cards or the empty state.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 15_000 });
    }

    /// <summary>Clicks the vacancy card whose title matches <paramref name="title"/> and waits for the read-only detail dialog to open.</summary>
    public async Task OpenCardAsync(string title)
    {
        var card = page.Locator(CardSelector).Filter(new() { HasText = title }).First;
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.ClickAsync();
        await page.Locator(DetailSelector).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator DetailDialog =>
        page.GetByRole(AriaRole.Dialog).Filter(new() { Has = page.Locator(DetailSelector) });

    public Task<bool> IsDetailVisibleAsync() =>
        page.Locator(DetailSelector).WaitUntilVisibleAsync();

    public async Task<string?> GetDetailTitleAsync() =>
        (await page.Locator($"{DetailSelector} .vacancy-detail-title").TextContentAsync())?.Trim();

    public async Task<string?> GetDetailDescriptionAsync() =>
        (await page.Locator($"{DetailSelector} .vacancy-detail-description").TextContentAsync())?.Trim();

    /// <summary>
    /// Count of buttons in the detail dialog whose accessible name matches <paramref name="name"/>
    /// — used to assert the read-only dialog has no "Apply"/"Save" action.
    /// </summary>
    public Task<int> DetailButtonCountAsync(string name) =>
        DetailDialog.GetByRole(AriaRole.Button, new() { Name = name }).CountAsync();
}
