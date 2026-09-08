using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee-facing Employee Directory.
/// Route: /companies/{CompanyId:guid}/employees/directory
///
/// Any authenticated employee of the company can view it. The page renders a search box
/// (data-testid="directory-search"), two SfDropDownList filters ("All departments" /
/// "All locations"), and a grid of cards (data-testid="directory-employee-card"). Clicking a
/// card opens a read-only SfDialog (data-testid="directory-detail").
/// </summary>
public sealed class EmployeeDirectoryPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/directory");
        await WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits for the interactive render: the search box is present and at least one card (or the
    /// empty state) has rendered, so the async employee load has actually completed.
    /// </summary>
    public async Task WaitForInteractiveAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='directory-search']", new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(
            "[data-testid='directory-employee-card'], :text('No employees found')",
            new() { Timeout = 20_000 });
    }

    public ILocator Heading => page.GetByRole(AriaRole.Heading, new() { Name = "Employee Directory" });

    // Syncfusion SfTextBox splats arbitrary attributes (data-testid) straight onto its underlying
    // <input>, not onto a wrapper — so match the input directly, with the descendant form as a
    // fallback in case a future control wraps it.
    private ILocator SearchBox => page.Locator(
        "input[data-testid='directory-search'], [data-testid='directory-search'] input").First;

    private ILocator Cards => page.Locator("[data-testid='directory-employee-card']");

    /// <summary>
    /// Types <paramref name="term"/> into the search box (search-on-change) and waits for the card
    /// list to settle to the expected count — either at least one match, or the empty state.
    /// </summary>
    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        // SfTextBox raises ValueChange on the native "change" event (blur), not on the "input" event
        // FillAsync dispatches — press Tab to blur and actually trigger OnSearchChanged.
        await SearchBox.PressAsync("Tab");
        // search-on-change fires a Blazor re-render; wait for the resulting list to settle rather
        // than an arbitrary pause.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(
            "[data-testid='directory-employee-card'], :text('No employees found')",
            new() { Timeout = 15_000 });
    }

    public Task<int> CardCount() => Cards.CountAsync();

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.GetByText("No employees found").IsVisibleAsync();

    public ILocator CardByName(string name) =>
        Cards.Filter(new() { HasText = name }).First;

    /// <summary>Clicks the card for <paramref name="name"/> and waits for the read-only detail dialog.</summary>
    public async Task<ILocator> OpenCardAsync(string name)
    {
        var card = CardByName(name);
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.ClickAsync();

        var dialog = page.Locator("[data-testid='directory-detail']");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return dialog;
    }
}
