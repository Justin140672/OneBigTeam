using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class OperationalAlertDetailsPage(IPage page, string baseUrl)
{
    public async Task GotoAsync(Guid id)
    {
        await page.GotoAsync($"{baseUrl}/operational-alerts/{id}");
        await page.WaitForSelectorAsync(".details-panel, .dashboard-error", new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public async Task<string> FieldAsync(string label)
    {
        var dd = page.Locator($"dt:text-is('{label}') + dd").First;
        await dd.WaitForAsync(new() { Timeout = 10_000 });
        return ((await dd.TextContentAsync()) ?? string.Empty).Trim();
    }

    public Task<string> SeverityAsync() => FieldAsync("Severity");
    public Task<string> CategoryAsync() => FieldAsync("Category");
    public Task<string> StatusAsync() => FieldAsync("Status");
    public Task<string> SummaryAsync() => FieldAsync("Summary");
    public Task<string> CompanyIdAsync() => FieldAsync("Company id");
    public Task<string> ResolutionNoteAsync() => FieldAsync("Resolution note");

    private ILocator ResolveButton =>
        page.Locator(".admin-actions-buttons").GetByRole(AriaRole.Button, new() { Name = "Resolve alert" });

    private ILocator ResolveDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Resolve alert" });

    public Task<bool> ResolveButtonVisibleAsync() => ResolveButton.IsVisibleAsync();

    public async Task ResolveAsync(string note)
    {
        await ResolveButton.ClickAsync();
        await ResolveDialog.WaitForAsync(new() { Timeout = 15_000 });

        await ResolveDialog.Locator("#admin-action-reason").FillAsync(note);
        await page.Keyboard.PressAsync("Tab");

        await ResolveDialog.GetByRole(AriaRole.Button, new() { Name = "Resolve alert", Exact = true }).ClickAsync();

        await page.WaitForSelectorAsync(".admin-action-success, .admin-action-error", new() { Timeout = 20_000 });
        await ResolveDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task OpenResolveDialogAsync()
    {
        await ResolveButton.ClickAsync();
        await ResolveDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task ClickResolveConfirmAsync() =>
        ResolveDialog.GetByRole(AriaRole.Button, new() { Name = "Resolve alert", Exact = true }).ClickAsync();

    public Task<string?> DialogValidationErrorAsync() =>
        ResolveDialog.Locator(".admin-action-error").TextContentAsync();

    public Task<bool> ResolveDialogVisibleAsync() => ResolveDialog.IsVisibleAsync();

    public Task<bool> SuccessVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public async Task<bool> AlreadyResolvedVisibleAsync()
    {
        var error = page.Locator(".admin-action-error");
        if (!await error.IsVisibleAsync())
            return false;
        var text = (await error.TextContentAsync()) ?? string.Empty;
        return text.Contains("already been resolved", StringComparison.OrdinalIgnoreCase);
    }
}
