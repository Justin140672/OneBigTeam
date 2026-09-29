using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CustomerDetailsPage(IPage page, string baseUrl)
{
    private const string ResolvedSelector = ".details-grid, .dashboard-error";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/customers/{companyId}");
        await page.WaitForSelectorAsync(ResolvedSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsLoadingAsync() =>
        page.GetByText("Loading…").IsVisibleAsync();

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<string?> GetErrorBannerTextAsync() =>
        page.Locator(".dashboard-error").TextContentAsync();

    public Task<string?> GetCompanyNameAsync() => GetKeyValueAsync("Company information", "Company name");

    public Task<string?> GetStatusAsync() => GetKeyValueAsync("Company information", "Status");

    public Task<string?> GetSubscriptionStatusAsync() => GetKeyValueAsync("Subscription", "Status");

    public Task<string?> GetTrialStartedAsync() => GetKeyValueAsync("Subscription", "Trial started");

    public Task<string?> GetTrialExpiresAsync() => GetKeyValueAsync("Subscription", "Trial expires");

    public Task<string?> GetCurrentPeriodEndAsync() => GetKeyValueAsync("Subscription", "Current period end");

    public Task<string?> GetCancelAtPeriodEndAsync() => GetKeyValueAsync("Subscription", "Cancel at period end");

    public Task<string?> GetMonthlyChargeAsync() => GetKeyValueAsync("Current pricing", "Monthly charge");

    public Task<string?> GetActiveEmployeeCountAsync() => GetStatCardValueAsync("Active employees");

    public Task<string?> GetTotalEmployeeCountAsync() => GetStatCardValueAsync("Total employees");

    public Task<string?> GetStorageUsedAsync() => GetStatCardValueAsync("Storage used");

    public Task<string?> GetFilesStoredAsync() => GetStatCardValueAsync("Files stored");

    public Task<bool> HasSettingsConfiguredAsync() =>
        GetSection("Company settings").Locator("dl.details-kv").IsVisibleAsync();

    public Task<bool> ShowsNoSettingsConfiguredMessageAsync() =>
        GetSection("Company settings").GetByText("No settings configured yet.").IsVisibleAsync();

    public Task<string?> GetSettingValueAsync(string label) => GetKeyValueAsync("Company settings", label);

    public Task<bool> IsBillingHistoryPanelVisibleAsync() =>
        GetSection("Billing history").IsVisibleAsync();

    public Task<string?> GetBillingHistoryTextAsync() =>
        GetSection("Billing history").Locator(".details-panel-unavailable").TextContentAsync();

    public Task<bool> IsLoginHistoryPanelVisibleAsync() =>
        GetSection("Login history").IsVisibleAsync();

    public Task<string?> GetLoginHistoryTextAsync() =>
        GetSection("Login history").Locator(".details-panel-unavailable").TextContentAsync();

    public ILocator BackToCustomersLink => page.GetByRole(AriaRole.Link, new() { Name = "Back to customers" });

    // Ticket 16 — links from the read-only details page into the (also read-mostly, save-for-its
    // status editor) support request queue for this company.
    public ILocator OpenSupportRequestsLink =>
        page.GetByRole(AriaRole.Link, new() { Name = "Open support requests" });

    public Task ClickBackToCustomersAsync() => BackToCustomersLink.ClickAsync();

    public ILocator ScheduleDeletionButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Schedule deletion" });

    public async Task ClickScheduleDeletionAsync()
    {
        await ScheduleDeletionButton.ClickAsync();
        await ScheduleDeletionDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    private ILocator ScheduleDeletionDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule deletion" });

    public Task<bool> IsScheduleDeletionDialogVisibleAsync() => ScheduleDeletionDialog.IsVisibleAsync();

    public async Task FillScheduleDeletionReasonAsync(string reason)
    {
        await ScheduleDeletionDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickScheduleDeletionConfirmAsync() =>
        ScheduleDeletionDialog.GetByRole(AriaRole.Button, new() { Name = "Schedule deletion", Exact = true }).ClickAsync();

    public Task<string?> GetScheduleDeletionValidationErrorAsync() =>
        ScheduleDeletionDialog.Locator(".admin-action-error").TextContentAsync();

    public Task<bool> IsSubscriptionActionSuccessVisibleAsync() =>
        GetSection("Subscription management").Locator(".admin-action-success").IsVisibleAsync();

    public ILocator LoginAsCustomerButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Login as customer" });

    public async Task ClickLoginAsCustomerAsync()
    {
        await LoginAsCustomerButton.ClickAsync();
        await LoginAsCustomerDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    private ILocator LoginAsCustomerDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Login as customer" });

    public Task<bool> IsLoginAsCustomerDialogVisibleAsync() => LoginAsCustomerDialog.IsVisibleAsync();

    public async Task FillLoginAsCustomerReasonAsync(string reason)
    {
        await LoginAsCustomerDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickLoginAsCustomerConfirmAsync() =>
        LoginAsCustomerDialog.GetByRole(AriaRole.Button, new() { Name = "Generate access token" }).ClickAsync();

    public Task ClickLoginAsCustomerCancelAsync() =>
        LoginAsCustomerDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public Task<string?> GetLoginAsCustomerDialogValidationErrorAsync() =>
        LoginAsCustomerDialog.Locator(".admin-action-error").TextContentAsync();

    public Task<bool> IsSupportSessionSuccessVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<string?> GetSupportSessionSuccessTextAsync() =>
        page.Locator(".admin-action-success").TextContentAsync();

    public Task<bool> IsSupportSessionErrorVisibleAsync() =>
        page.Locator("section.details-panel.admin-actions-panel").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Heading, new() { Name = "Login as customer", Exact = true }),
        }).Locator(".admin-action-error").IsVisibleAsync();

    public Task<bool> IsAutomaticSignInNotYetImplementedNoteVisibleAsync() =>
        page.GetByText("Full automatic sign-in is not yet implemented").WaitUntilVisibleAsync();

    private ILocator GetSection(string headingText) =>
        page.Locator("section.details-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = headingText, Exact = true }) });

    private async Task<string?> GetKeyValueAsync(string headingText, string key)
    {
        var value = GetSection(headingText)
            .Locator($"xpath=.//dt[normalize-space(text())='{key}']/following-sibling::dd[1]");
        return (await value.TextContentAsync())?.Trim();
    }

    private async Task<string?> GetStatCardValueAsync(string label)
    {
        var card = page.Locator(".stat-cards:not(.billing-stat-cards) .stat-card").Filter(new() { HasText = label });
        return (await card.Locator(".stat-value").TextContentAsync())?.Trim();
    }
}
