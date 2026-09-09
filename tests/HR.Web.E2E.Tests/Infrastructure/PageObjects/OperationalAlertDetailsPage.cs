using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's OperationalAlertDetails.razor (/operational-alerts/{Id:guid}).
/// Renders the full alert as a set of <c>&lt;dt&gt;/&lt;dd&gt;</c> pairs. When the alert is not yet
/// resolved it shows a "Resolve alert" <c>SfButton</c> which opens the shared
/// <c>AdminActionConfirmDialog</c> (title "Resolve alert", mandatory reason textarea, min 5 chars —
/// see AdminActionConfirmDialog.razor). On success ".admin-action-success" appears and the alert
/// reloads as Resolved (button gone); a 409 surfaces the friendly ".admin-action-error"
/// "already been resolved" message.
/// </summary>
public sealed class OperationalAlertDetailsPage(IPage page, string baseUrl)
{
    public async Task GotoAsync(Guid id)
    {
        await page.GotoAsync($"{baseUrl}/operational-alerts/{id}");
        await page.WaitForSelectorAsync(".details-panel, .dashboard-error", new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    /// <summary>Value of the &lt;dd&gt; immediately following the &lt;dt&gt; whose text is exactly <paramref name="label"/>.</summary>
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

    /// <summary>
    /// Full resolve flow: open the confirm dialog, enter <paramref name="note"/>, confirm, and wait
    /// for the action to settle on exactly one result message with the dialog closed.
    /// </summary>
    public async Task ResolveAsync(string note)
    {
        await ResolveButton.ClickAsync();
        await ResolveDialog.WaitForAsync(new() { Timeout = 15_000 });

        // AdminActionConfirmDialog's SfTextBox binds on blur/change, not raw input — Tab to commit.
        await ResolveDialog.Locator("#admin-action-reason").FillAsync(note);
        await page.Keyboard.PressAsync("Tab");

        await ResolveDialog.GetByRole(AriaRole.Button, new() { Name = "Resolve alert", Exact = true }).ClickAsync();

        await page.WaitForSelectorAsync(".admin-action-success, .admin-action-error", new() { Timeout = 20_000 });
        await ResolveDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Opens the dialog and clicks confirm without entering a note — used to assert the min-length guard.</summary>
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
