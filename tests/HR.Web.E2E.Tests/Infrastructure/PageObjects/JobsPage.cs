using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

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

    public Task<bool> AreJobSectionsVisibleAsync() =>
        page.GetByRole(AriaRole.Heading, new() { Name = "Failed jobs" }).IsVisibleAsync();

    private ILocator FailedSection =>
        page.Locator("section.admin-actions-panel").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Heading, new() { Name = "Failed jobs" }),
        });

    public Task<bool> IsNoFailedJobsEmptyStateVisibleAsync() =>
        FailedSection.GetByText("No failed jobs.").IsVisibleAsync();

    public Task<int> GetFailedJobRowCountAsync() =>
        FailedSection.Locator(".e-grid .e-row").CountAsync();

    public async Task<string?> GetActionMessageAsync()
    {
        var msg = FailedSection.Locator(".admin-action-success, .admin-action-error");
        return await msg.IsVisibleAsync() ? (await msg.TextContentAsync())?.Trim() : null;
    }

    public Task<bool> IsActionErrorVisibleAsync() =>
        FailedSection.Locator(".admin-action-error").IsVisibleAsync();


    private ILocator RetryDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Retry failed job" });

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
