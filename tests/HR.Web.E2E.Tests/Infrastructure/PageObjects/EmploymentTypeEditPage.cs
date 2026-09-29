using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmploymentTypeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employment-types/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employmentTypeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employment-types/{employmentTypeId}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public Task FillNameAsync(string name) =>
        FillTextBoxAsync("e.g. Permanent, Contractor", name);

    public Task FillDescriptionAsync(string description) =>
        FillTextBoxAsync("Optional description", description);

    private async Task FillTextBoxAsync(string placeholder, string value)
    {
        var input = page.GetByPlaceholder(placeholder);
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await input.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await input.PressSequentiallyAsync(value, new() { Delay = 20 });
            await page.Keyboard.PressAsync("Tab");

            if (await input.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    private static readonly PageWaitForURLOptions CommitNav = new()
    {
        Timeout = 30_000,
        WaitUntil = WaitUntilState.Commit,
    };

    private async Task WaitForListGridAsync() =>
        await page.WaitForSelectorAsync(
            ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger", new() { Timeout = 30_000 });

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", CommitNav);
        await WaitForListGridAsync();
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("e.g. Permanent, Contractor").InputValueAsync();

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

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

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", CommitNav);
        await WaitForListGridAsync();
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", CommitNav);
        await WaitForListGridAsync();
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/employment-types", CommitNav);
        await WaitForListGridAsync();
    }
}
