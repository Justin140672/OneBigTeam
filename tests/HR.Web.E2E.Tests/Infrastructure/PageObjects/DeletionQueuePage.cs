using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's DeletionQueue.razor (/deletion-queue) — the platform-admin-only
/// list of companies that currently have, or have ever had, a permanent deletion scheduled. Each
/// pending row exposes "Cancel deletion" / "Execute now" actions, both going through the shared
/// AdminActionConfirmDialog (mandatory reason, min 5 chars) also used by CustomerDetails.razor's
/// Subscription management panel — see CustomerDetailsPage for that dialog's sibling usage.
///
/// Executing/cancelling here are status-only, reversible-in-principle actions and never destroy
/// real employee/document/company data — see DeletionQueue.razor's intro copy and
/// AdminActionConfirmDialog's "Execute now" warning text, which some tests assert on directly.
/// </summary>
public sealed class DeletionQueuePage(IPage page, string baseUrl)
{
    // DeletionQueue.razor renders exactly one of: loading text, the "not authorised" dashboard-error
    // div, the empty-state paragraph, or the table — wait for any "settled" state.
    private const string SettledSelector = ".dashboard-error, .activity-empty, table.billing-history-table";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/deletion-queue");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".activity-empty").IsVisibleAsync();

    public Task<bool> IsTableVisibleAsync() =>
        page.Locator("table.billing-history-table").IsVisibleAsync();

    private ILocator RowByCompany(string companyNameFragment) =>
        page.Locator("table.billing-history-table tbody tr").Filter(new() { HasText = companyNameFragment });

    public async Task<bool> HasCompanyAsync(string companyNameFragment)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        return await RowByCompany(companyNameFragment).First.IsVisibleAsync();
    }

    public Task<string?> GetStatusPillTextAsync(string companyNameFragment) =>
        RowByCompany(companyNameFragment).Locator(".status-pill").TextContentAsync();

    public Task<bool> IsPendingAsync(string companyNameFragment) =>
        RowByCompany(companyNameFragment).Locator(".status-pill-pending").IsVisibleAsync();

    public Task<bool> IsCancelledAsync(string companyNameFragment) =>
        RowByCompany(companyNameFragment).Locator(".status-pill-cancelled").IsVisibleAsync();

    public Task<bool> IsExecutedAsync(string companyNameFragment) =>
        RowByCompany(companyNameFragment).Locator(".status-pill-executed").IsVisibleAsync();

    public Task<string?> GetCountdownTextAsync(string companyNameFragment)
    {
        // Countdown is rendered in the 3rd <td> (Company, Scheduled, Countdown, Status, actions).
        var cell = RowByCompany(companyNameFragment).Locator("td").Nth(2);
        return cell.TextContentAsync();
    }

    public async Task ClickCancelDeletionAsync(string companyNameFragment)
    {
        await RowByCompany(companyNameFragment).GetByRole(AriaRole.Button, new() { Name = "Cancel deletion" }).ClickAsync();
        await CancelDeletionDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickExecuteNowAsync(string companyNameFragment)
    {
        await RowByCompany(companyNameFragment).GetByRole(AriaRole.Button, new() { Name = "Execute now" }).ClickAsync();
        await ExecuteDeletionDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    // The shared AdminActionConfirmDialog, addressed by its per-action title so tests can
    // disambiguate "Cancel deletion" from "Begin controlled deletion" — see AdminActionConfirmDialog.razor
    // and DeletionQueue.razor's DialogTitle.
    private ILocator DialogByTitle(string title) => page.GetByRole(AriaRole.Dialog, new() { Name = title });

    public ILocator CancelDeletionDialog => DialogByTitle("Cancel deletion");

    public ILocator ExecuteDeletionDialog => DialogByTitle("Begin controlled deletion");

    public Task<bool> IsCancelDeletionDialogVisibleAsync() => CancelDeletionDialog.IsVisibleAsync();

    public Task<bool> IsExecuteDeletionDialogVisibleAsync() => ExecuteDeletionDialog.IsVisibleAsync();

    public Task<string?> GetExecuteDeletionWarningTextAsync() =>
        ExecuteDeletionDialog.Locator(".admin-action-warning").TextContentAsync();

    public async Task FillCancelDeletionReasonAsync(string reason)
    {
        await CancelDeletionDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillExecuteDeletionReasonAsync(string reason)
    {
        await ExecuteDeletionDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickCancelDeletionConfirmAsync() =>
        CancelDeletionDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel deletion", Exact = true }).ClickAsync();

    public Task ClickExecuteDeletionConfirmAsync() =>
        ExecuteDeletionDialog.GetByRole(AriaRole.Button, new() { Name = "Execute deletion", Exact = true }).ClickAsync();

    public Task<string?> GetCancelDeletionValidationErrorAsync() =>
        CancelDeletionDialog.Locator(".admin-action-error").TextContentAsync();

    public Task<string?> GetExecuteDeletionValidationErrorAsync() =>
        ExecuteDeletionDialog.Locator(".admin-action-error").TextContentAsync();

    public Task<bool> IsActionSuccessVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<bool> IsActionErrorVisibleAsync() =>
        page.Locator(".admin-action-error").IsVisibleAsync();
}
