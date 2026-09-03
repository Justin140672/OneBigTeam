using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's Jobs.razor (/jobs) — the platform-admin-only background job
/// dashboard (Scheduled / Running / Failed sections). The only mutating action on the page is
/// "Retry" on a failed job, which opens the shared <see cref="AdminActionConfirmDialog"/>
/// ("Retry failed job") requiring a reason before it calls BackgroundJobsService.RetryJobAsync.
///
/// Jobs.razor renders exactly one of: the "Loading…" text, the "not authorised / couldn't be
/// loaded" dashboard-error div, the "storage unavailable" dashboard-error div, or the three job
/// sections — GoToAsync waits for whichever settles first.
/// </summary>
public sealed class JobsPage(IPage page, string baseUrl)
{
    private const string SettledSelector = ".dashboard-error, .admin-actions-panel";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/jobs");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<string?> GetErrorBannerTextAsync() =>
        page.Locator(".dashboard-error").TextContentAsync();

    /// <summary>True once the three job sections rendered (i.e. the authorised, storage-available state).</summary>
    public Task<bool> AreJobSectionsVisibleAsync() =>
        page.GetByRole(AriaRole.Heading, new() { Name = "Failed jobs" }).IsVisibleAsync();

    private ILocator FailedSection =>
        page.Locator("section.admin-actions-panel").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Heading, new() { Name = "Failed jobs" }),
        });

    public Task<bool> IsNoFailedJobsEmptyStateVisibleAsync() =>
        FailedSection.GetByText("No failed jobs.").IsVisibleAsync();

    /// <summary>Number of failed-job rows currently rendered in the Failed jobs grid.</summary>
    public Task<int> GetFailedJobRowCountAsync() =>
        FailedSection.Locator(".e-grid .e-row").CountAsync();

    /// <summary>The action-result message paragraph shown above the Failed jobs grid, or null if none is shown.</summary>
    public async Task<string?> GetActionMessageAsync()
    {
        var msg = FailedSection.Locator(".admin-action-success, .admin-action-error");
        return await msg.IsVisibleAsync() ? (await msg.TextContentAsync())?.Trim() : null;
    }

    public Task<bool> IsActionErrorVisibleAsync() =>
        FailedSection.Locator(".admin-action-error").IsVisibleAsync();

    // ── Retry confirmation dialog (AdminActionConfirmDialog, Title="Retry failed job") ──

    private ILocator RetryDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Retry failed job" });

    /// <summary>Clicks the first failed row's "Retry" button and waits for the confirm dialog to open.</summary>
    public async Task OpenRetryDialogForFirstFailedJobAsync()
    {
        await FailedSection.Locator(".e-row").First
            .GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await RetryDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsRetryDialogVisibleAsync() => RetryDialog.IsVisibleAsync();

    public async Task FillRetryReasonAsync(string reason)
    {
        await RetryDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks the dialog's "Retry job" confirm button (does not wait for close — an invalid reason keeps it open).</summary>
    public Task ClickRetryConfirmAsync() =>
        RetryDialog.GetByRole(AriaRole.Button, new() { Name = "Retry job" }).ClickAsync();

    public async Task ClickRetryCancelAsync()
    {
        await RetryDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await RetryDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetRetryValidationErrorAsync()
    {
        var error = RetryDialog.Locator(".admin-action-error");
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.TextContentAsync())?.Trim();
    }
}
