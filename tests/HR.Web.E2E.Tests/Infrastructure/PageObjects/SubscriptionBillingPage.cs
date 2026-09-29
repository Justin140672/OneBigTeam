using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Subscription & Billing page (SubscriptionOverview.razor at /subscription).
/// Encapsulates interactions with subscription status display, plan information, billing dates,
/// and action buttons (Start subscription, Manage billing, Resume, Cancel).
/// </summary>
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

    /// <summary>
    /// Returns the visible subscription status text (e.g., "Active", "Trial", "Trial expired", "Read-only").
    /// This is the humanized display of the SubscriptionStatus enum.
    /// </summary>
    public async Task<string?> GetSubscriptionStatusAsync()
    {
        // Status is displayed in a card body with label "Status" followed by the value
        var statusSection = page.Locator(".card-body").First;
        var statusLabel = statusSection.Locator("label:has-text('Status')").First;
        var statusValue = statusLabel.Locator("xpath=following-sibling::p[1]");
        return (await statusValue.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the plan name display (e.g., "Starter", "Professional", "Enterprise").
    /// </summary>
    public async Task<string?> GetPlanAsync()
    {
        var planSection = page.Locator(".card-body").First;
        var planLabel = planSection.Locator("label:has-text('Plan')").First;
        var planValue = planLabel.Locator("xpath=following-sibling::p[1]");
        return (await planValue.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the next billing date display (e.g., "1/15/2026" or "Not yet billed").
    /// </summary>
    public async Task<string?> GetNextBillingDateAsync()
    {
        var dateSection = page.Locator(".card-body").First;
        var dateLabel = dateSection.Locator("label:has-text('Next Billing Date')").First;
        var dateValue = dateLabel.Locator("xpath=following-sibling::p[1]");
        return (await dateValue.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the active employee count display.
    /// </summary>
    public async Task<string?> GetActiveEmployeeCountAsync()
    {
        var empSection = page.Locator(".card-body").First;
        var empLabel = empSection.Locator("label:has-text('Active Employees')").First;
        var empValue = empLabel.Locator("xpath=following-sibling::p[1]");
        return (await empValue.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns trial days remaining if in trial status (e.g., "7").
    /// Returns null if trial days row is not visible (not in trial).
    /// </summary>
    public async Task<string?> GetTrialDaysRemainingAsync()
    {
        var trialSection = page.Locator(".card-body").First;
        var trialLabel = trialSection.Locator("label:has-text('Trial Days Remaining')");

        if (!await trialLabel.IsVisibleAsync())
            return null;

        var trialValue = trialLabel.Locator("xpath=following-sibling::p[1]");
        return (await trialValue.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns true if the "Start subscription" button is visible.
    /// This button appears when subscription is in Trial or TrialExpired status.
    /// </summary>
    public Task<bool> HasStartSubscriptionButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Start subscription" }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the "Manage billing" button is visible and enabled.
    /// This button appears when subscription is Active (not in trial).
    /// </summary>
    public Task<bool> HasManageBillingButtonAsync() =>
        IsVisibleAndEnabledAsync("Manage billing");

    /// <summary>
    /// Returns true if the "Resume subscription" button is visible and enabled.
    /// This button appears when subscription is marked for cancellation at period end.
    /// </summary>
    public Task<bool> HasResumeButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Resume subscription" }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the "Cancel subscription" button is visible and enabled.
    /// This button appears when subscription is Active and not already cancelled.
    /// </summary>
    public Task<bool> HasCancelButtonAsync() =>
        IsVisibleAndEnabledAsync("Cancel subscription");

    /// <summary>
    /// IsEnabledAsync() requires the element to actually exist — unlike IsVisibleAsync(), which
    /// gracefully returns false when nothing matches, IsEnabledAsync() errors/hangs once the
    /// button is genuinely absent from the DOM (e.g. after a state transition swaps it out for a
    /// different button entirely, as Cancel -> Resume does). Check visibility first so a caller
    /// asking "is this button here and usable" gets a clean false instead of an exception when the
    /// button simply isn't there at all.
    /// </summary>
    private async Task<bool> IsVisibleAndEnabledAsync(string buttonName)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = buttonName });
        return await button.IsVisibleAsync() && await button.IsEnabledAsync();
    }

    /// <summary>
    /// Clicks the "Start subscription" button and waits for navigation to Stripe checkout.
    /// The page navigates away with forceLoad: true, so we expect the URL to change.
    /// </summary>
    public async Task ClickStartSubscriptionAsync()
    {
        var startButton = page.GetByRole(AriaRole.Button, new() { Name = "Start subscription" });
        await startButton.ClickAsync();
        // The page navigates away to Stripe checkout with forceLoad: true,
        // so we wait for a URL change (should go to Stripe test stub or mock URL)
        await page.WaitForURLAsync(url => !url.Contains("/subscription"), new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the "Manage billing" button and waits for navigation to Stripe billing portal.
    /// Similar to checkout, this navigates away with forceLoad: true.
    /// </summary>
    public async Task ClickManageBillingAsync()
    {
        var billingButton = page.GetByRole(AriaRole.Button, new() { Name = "Manage billing" });
        await billingButton.ClickAsync();
        // The page navigates away to Stripe billing portal with forceLoad: true
        await page.WaitForURLAsync(url => !url.Contains("/subscription"), new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the "Resume subscription" button and waits for the page to settle.
    /// Unlike checkout/billing portal, this is an in-page action that updates state.
    /// </summary>
    public async Task ClickResumeAsync()
    {
        var resumeButton = page.GetByRole(AriaRole.Button, new() { Name = "Resume subscription" });
        await resumeButton.ClickAsync();
        // Wait for the success message or state change to appear
        await page.Locator(".alert-success").WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the "Cancel subscription" button, which shows a confirmation dialog.
    /// </summary>
    public async Task ClickCancelAsync()
    {
        var cancelButton = page.GetByRole(AriaRole.Button, new() { Name = "Cancel subscription" });
        await cancelButton.ClickAsync();
        // Wait for the confirmation dialog to appear
        await IsCancelConfirmDialogVisibleAsync();
    }

    /// <summary>
    /// Returns true if the cancel subscription confirmation dialog is visible.
    /// </summary>
    public Task<bool> IsCancelConfirmDialogVisibleAsync() =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" }).IsVisibleAsync();

    /// <summary>
    /// Confirms the cancel subscription action by clicking "Cancel subscription" in the dialog.
    /// </summary>
    public async Task ConfirmCancelAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" });
        var confirmButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel subscription", Exact = true });
        await confirmButton.ClickAsync();
        // Wait for success message
        await page.Locator(".alert-success").WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Cancels the cancel subscription dialog by clicking "Keep subscription".
    /// </summary>
    public async Task CancelCancelAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" });
        var keepButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Keep subscription" });
        await keepButton.ClickAsync();
        // Dialog should disappear
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel subscription" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>
    /// Returns any success alert text on the page (e.g., "Your subscription is scheduled to be cancelled...").
    /// Returns null if no success alert is visible.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync()
    {
        var alert = page.Locator(".alert-success");
        if (!await alert.IsVisibleAsync())
            return null;
        return (await alert.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns any error alert text on the page.
    /// Returns null if no error alert is visible.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync()
    {
        var alert = page.Locator(".alert-danger");
        if (!await alert.IsVisibleAsync())
            return null;
        return (await alert.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns true if there is a warning alert about subscription ending (CancelAtPeriodEnd = true).
    /// </summary>
    public Task<bool> HasCancellationWarningAsync() =>
        page.Locator(".alert-warning:has-text('Your subscription will end')").IsVisibleAsync();
}
