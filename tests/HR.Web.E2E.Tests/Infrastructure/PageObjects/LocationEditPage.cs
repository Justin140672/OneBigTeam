using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location create/edit page.
/// Routes: /companies/{id}/locations/new  and  /companies/{id}/locations/{id}
/// </summary>
public sealed class LocationEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/locations/new");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid locationId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/locations/{locationId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("Location name").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Selects a location type from the Syncfusion dropdown by filtering on a fragment of its
    /// name. Follows the popup-hidden-wait + committed-value-assertion pattern used elsewhere
    /// for Syncfusion dropdowns (see VacancyDetailPage.SelectPositionProfileAsync) — popup-hidden
    /// alone can be a purely client-side JS close animation and isn't proof that Blazor's
    /// ValueChanged round-trip to the server actually committed the selected id yet.
    /// </summary>
    public async Task SelectLocationTypeAsync(string nameFragment)
    {
        var group = page.Locator(".mb-3").Filter(new() { HasText = "Location Type" }).First;
        await DropDownSelector.SelectAsync(page, group, nameFragment);

        await Assertions.Expect(group.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(nameFragment)), new() { Timeout = 10_000 });
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/locations", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message, .invalid-feedback").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("Location name").InputValueAsync();

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    // ── Description field (optional HrTextBox) — robust type-for-real technique, see
    // DocumentTypeEditPage.SetDescriptionAsync for the full rationale. ─────────────
    public async Task SetDescriptionAsync(string value)
    {
        var input = page.GetByPlaceholder("Optional description");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public Task<string> GetDescriptionAsync() =>
        page.GetByPlaceholder("Optional description").InputValueAsync();

    public async Task<string> WaitForDescriptionAsync(string expected)
    {
        var input = page.GetByPlaceholder("Optional description");
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    // ── Optimistic-concurrency conflict banner (Ticket 2) — shared <SaveConflictBanner>. ──
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }
}
