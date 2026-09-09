using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location type create/edit page.
/// Routes: /companies/{id}/location-types/new  and  /companies/{id}/location-types/{id}
/// </summary>
public sealed class LocationTypeEditPage(IPage page, string baseUrl)
{
    public async Task GoToEditAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/location-types/{id}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Office, Warehouse").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetNameAsync() =>
        page.GetByPlaceholder("e.g. Office, Warehouse").InputValueAsync();

    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

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

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the location-types list on success.
        await page.WaitForURLAsync("**/location-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
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
