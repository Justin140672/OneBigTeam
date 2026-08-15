using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's CustomerDetails.razor (/customers/{CompanyId}) — a fully
/// read-only single-customer view: company information, subscription, current pricing, employee
/// count / storage usage stat cards, a company settings summary (or a "no settings configured"
/// fallback), and two explanatory "not yet available" panels for billing/login history. There is
/// no create/edit/delete affordance on this page — it is view-only by design.
/// </summary>
public sealed class CustomerDetailsPage(IPage page, string baseUrl)
{
    // Rendered once _loading flips false, whichever branch (details vs. dashboard-error) applies.
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

    public Task ClickBackToCustomersAsync() => BackToCustomersLink.ClickAsync();

    // Subscription management panel — "Schedule deletion" action. Uses its own
    // AdminActionConfirmDialog instance (shared across all Subscription management buttons:
    // Extend trial / Cancel at period end / Reinstate / Force read-only / Resume service /
    // Schedule deletion), addressed here by its per-action dialog title. See CustomerDetails.razor's
    // DialogTitle switch.
    public ILocator ScheduleDeletionButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Schedule deletion" });

    public Task ClickScheduleDeletionAsync() => ScheduleDeletionButton.ClickAsync();

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

    // "Login as customer" support-session action — its own AdminActionConfirmDialog instance,
    // separate from the Subscription management panel's shared dialog. See CustomerDetails.razor.
    public ILocator LoginAsCustomerButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Login as customer" });

    public Task ClickLoginAsCustomerAsync() => LoginAsCustomerButton.ClickAsync();

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

    // Scoped to the top-level stat-cards row specifically (":not(.billing-stat-cards)") — the
    // Billing breakdown panel further down the page has its own, separate ".stat-cards
    // billing-stat-cards" row that also includes an "Active employees" card, so an unscoped
    // ".stat-card" filter matches both and throws a Playwright strict-mode violation.
    private async Task<string?> GetStatCardValueAsync(string label)
    {
        var card = page.Locator(".stat-cards:not(.billing-stat-cards) .stat-card").Filter(new() { HasText = label });
        return (await card.Locator(".stat-value").TextContentAsync())?.Trim();
    }
}
