using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SubscriptionBillingPage(IPage page, string baseUrl)
{
    private const string ResolvedSelector = ".card-header h5";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/subscription");
        await page.WaitForSelectorAsync(ResolvedSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsLoadingAsync() =>
        page.GetByText("Loading subscription…").IsVisibleAsync();

    public async Task<string?> GetSubscriptionStatusAsync()
    {
        var statusSection = page.Locator(".card-body").First;
        var statusLabel = statusSection.Locator("label:has-text('Status')").First;
        var statusValue = statusLabel.Locator("xpath=following-sibling::p[1]");
        return (await statusValue.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetPlanAsync()
    {
        var planSection = page.Locator(".card-body").First;
        var planLabel = planSection.Locator("label:has-text('Plan')").First;
        var planValue = planLabel.Locator("xpath=following-sibling::p[1]");
        return (await planValue.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetNextBillingDateAsync()
    {
        var dateSection = page.Locator(".card-body").First;
        var dateLabel = dateSection.Locator("label:has-text('Next Billing Date')").First;
        var dateValue = dateLabel.Locator("xpath=following-sibling::p[1]");
        return (await dateValue.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetActiveEmployeeCountAsync()
    {
        var empSection = page.Locator(".card-body").First;
        var empLabel = empSection.Locator("label:has-text('Active Employees')").First;
        var empValue = empLabel.Locator("xpath=following-sibling::p[1]");
        return (await empValue.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetTrialDaysRemainingAsync()
    {
        var trialSection = page.Locator(".card-body").First;
        var trialLabel = trialSection.Locator("label:has-text('Trial Days Remaining')");

        if (!await trialLabel.IsVisibleAsync())
            return null;

        var trialValue = trialLabel.Locator("xpath=following-sibling::p[1]");
        return (await trialValue.TextContentAsync())?.Trim();
    }

    public Task<bool> HasStartSubscriptionButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Start subscription" }).IsVisibleAsync();

    public Task<bool> HasManageBillingButtonAsync() =>
        IsVisibleAndEnabledAsync("Manage billing");

    public Task<bool> HasResumeButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Resume subscription" }).IsVisibleAsync();

    public Task<bool> HasCancelButtonAsync() =>
        IsVisibleAndEnabledAsync("Cancel subscription");

    private async Task<bool> IsVisibleAndEnabledAsync(string buttonName)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = buttonName });
        return await button.IsVisibleAsync() && await button.IsEnabledAsync();
    }

    public async Task ClickStartSubscriptionAsync()
    {
        var startButton = page.GetByRole(AriaRole.Button, new() { Name = "Start subscription" });
        await startButton.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/subscription"), new() { Timeout = 15_000 });
    }

    public async Task ClickManageBillingAsync()
    {
        var billingButton = page.GetByRole(AriaRole.Button, new() { Name = "Manage billing" });
        await billingButton.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/subscription"), new() { Timeout = 15_000 });
    }

    public async Task ClickResumeAsync()
    {
        var resumeButton = page.GetByRole(AriaRole.Button, new() { Name = "Resume subscription" });
        await resumeButton.ClickAsync();
        await page.Locator(".alert-success").WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickCancelAsync()
    {
        var cancelButton = page.GetByRole(AriaRole.Button, new() { Name = "Cancel subscription" });
        await cancelButton.ClickAsync();
        await IsCancelConfirmDialogVisibleAsync();
    }

    /// <summary>
    /// Returns true once the "Cancel subscription" confirmation dialog is visible, or false if it
    /// never appears within the timeout. A plain IsVisibleAsync() snapshot right after the
    /// triggering click races the dialog's own open animation (RequestCancelAsync flips
    /// _showCancelConfirm, then SfDialog renders and animates in) — a caller that checks
    /// immediately can see it as not-yet-visible even though it's about to appear. Bound-wait for
    /// it instead, the same pattern used by CancelLeavingProcessDialog.OpenAsync and
    /// SidebarPage.IsSidebarVisibleAsync elsewhere in this suite.
    /// </summary>
    public async Task<bool> IsCancelConfirmDialogVisibleAsync()
    {
        try
        {
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ConfirmCancelAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" });
        var confirmButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel subscription", Exact = true });
        await confirmButton.ClickAsync();
        await page.Locator(".alert-success").WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task CancelCancelAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" });
        var keepButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Keep subscription" });
        await keepButton.ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<string?> GetSuccessMessageAsync()
    {
        var alert = page.Locator(".alert-success");
        if (!await alert.IsVisibleAsync())
            return null;
        return (await alert.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetErrorMessageAsync()
    {
        var alert = page.Locator(".alert-danger");
        if (!await alert.IsVisibleAsync())
            return null;
        return (await alert.TextContentAsync())?.Trim();
    }

    public Task<bool> HasCancellationWarningAsync() =>
        page.Locator(".alert-warning:has-text('Your subscription will end')").IsVisibleAsync();
}
