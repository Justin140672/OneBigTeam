using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SettingsPage(IPage page, string baseUrl)
{
    private const string SettledSelector = ".dashboard-error, #trial-length-days";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/settings");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsFormVisibleAsync() =>
        page.Locator("#trial-length-days").IsVisibleAsync();


    private ILocator TrialLengthInput => page.Locator("#trial-length-days");

    private ILocator DefaultMonthlyPriceInput => page.Locator("#default-monthly-price");

    private ILocator SupportEmailInput => page.Locator("#support-email");

    public async Task<string> GetTrialLengthAsync() => await TrialLengthInput.InputValueAsync();

    public async Task<string> GetDefaultMonthlyPriceAsync() => await DefaultMonthlyPriceInput.InputValueAsync();

    public async Task<string> GetSupportEmailAsync() => await SupportEmailInput.InputValueAsync();

    public async Task SetTrialLengthAsync(string value)
    {
        await TrialLengthInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");

        await page.WaitForTimeoutAsync(200);
    }

    public async Task SetDefaultMonthlyPriceAsync(string value)
    {
        await DefaultMonthlyPriceInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SetSupportEmailAsync(string value)
    {
        await SupportEmailInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }


    private ILocator MaintenanceModeCheckbox =>
        page.Locator(".settings-checkbox-field .e-checkbox-wrapper, .settings-checkbox-field input[type='checkbox']").First;

    private ILocator MaintenanceMessageInput => page.Locator("#maintenance-message");

    public Task<bool> IsMaintenanceModeCheckedAsync() =>
        page.Locator(".settings-checkbox-field input[type='checkbox']").First.IsCheckedAsync();

    public async Task ToggleMaintenanceModeAsync()
    {
        await MaintenanceModeCheckbox.ClickAsync();

        await page.WaitForTimeoutAsync(200);
    }

    public Task<bool> IsMaintenanceMessageVisibleAsync() => MaintenanceMessageInput.IsVisibleAsync();

    public async Task SetMaintenanceMessageAsync(string value)
    {
        await MaintenanceMessageInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetMaintenanceMessageAsync() => MaintenanceMessageInput.InputValueAsync();


    private ILocator FeatureFlagRows => page.Locator(".settings-flag-row");

    public Task<int> GetFeatureFlagRowCountAsync() => FeatureFlagRows.CountAsync();

    public async Task ClickAddFlagAsync()
    {
        var countBefore = await GetFeatureFlagRowCountAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Add flag" }).ClickAsync();

        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (await GetFeatureFlagRowCountAsync() > countBefore)
                return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    public async Task SetFlagNameAsync(int index, string name)
    {
        var input = FeatureFlagRows.Nth(index).Locator(".settings-flag-name input, input.settings-flag-name");
        await input.FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<bool> IsFlagEnabledAsync(int index) =>
        FeatureFlagRows.Nth(index).Locator("input[type='checkbox']").IsCheckedAsync();

    public async Task ToggleFlagEnabledAsync(int index)
    {
        await FeatureFlagRows.Nth(index).Locator(".e-checkbox-wrapper").ClickAsync();
    }

    public async Task RemoveFlagAsync(int index)
    {
        var countBefore = await GetFeatureFlagRowCountAsync();
        await FeatureFlagRows.Nth(index).GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();

        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (await GetFeatureFlagRowCountAsync() < countBefore)
                return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    public async Task<int> FindFlagRowIndexAsync(string name)
    {
        var count = await GetFeatureFlagRowCountAsync();
        for (var i = 0; i < count; i++)
        {
            var input = FeatureFlagRows.Nth(i).Locator(".settings-flag-name input, input.settings-flag-name");
            if (await input.InputValueAsync() == name)
                return i;
        }

        return -1;
    }


    private ILocator LastUpdatedSection =>
        page.Locator(".details-panel").Filter(new() { HasText = "Last updated" });

    public Task<string?> GetLastUpdatedWhenTextAsync() =>
        LastUpdatedSection.Locator("dd").First.TextContentAsync();


    public Task ClickSaveAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

    public ILocator SaveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Save platform settings" });

    public async Task FillDialogReasonAsync(string reason)
    {
        await SaveDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickDialogConfirmAsync() =>
        SaveDialog.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

    public async Task SaveAsync(string reason = "E2E: updating platform settings")
    {
        await ClickSaveAsync();
        await SaveDialog.WaitForAsync(new() { Timeout = 10_000 });
        await FillDialogReasonAsync(reason);
        await ClickDialogConfirmAsync();
    }

    public Task<bool> IsSuccessBannerVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<bool> IsErrorListVisibleAsync() =>
        page.Locator("ul.admin-action-error").IsVisibleAsync();

    public Task<string?> GetErrorListTextAsync() =>
        page.Locator("ul.admin-action-error").TextContentAsync();
}
