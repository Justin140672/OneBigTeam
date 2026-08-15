using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Cancel Leaving Process" dialog (CancelLeavingProcessDialog.razor),
/// opened via the "Cancel Leaving Process" button in the Leaving tab's card header (only visible
/// while the leaving process's Status is "InProgress" — see
/// <see cref="EmployeeLeavingTab.HasCancelButtonAsync"/>). Requires a free-text Cancellation
/// Reason and, when the employee already has an offboarding plan in progress (which happens
/// automatically as a side effect of Start Leaving Process — see
/// StartLeavingProcessHandler/IOffboardingPlanCoordinator.StartAsync), shows a stronger warning
/// that outstanding offboarding tasks will also be cancelled.
///
/// Follows the standalone-page-object-per-dialog pattern established by
/// <see cref="StartLeavingProcessDialog"/> / <see cref="AmendLeavingProcessDialog"/>.
/// </summary>
public sealed class CancelLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel Leaving Process" });

    /// <summary>
    /// Clicks the Leaving tab's "Cancel Leaving Process" button and waits for the dialog to open.
    /// </summary>
    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>
    /// Returns true if the stronger "Offboarding has already started ... will also cancel the
    /// outstanding offboarding tasks" warning is visible (CancelLeavingProcessDialog's
    /// ShowOffboardingTab-gated alert-warning, sourced from EmployeeEdit's own
    /// _showOffboardingTab).
    /// </summary>
    public async Task<bool> HasOffboardingTasksWarningAsync() =>
        await Dialog.Locator(".alert-warning").IsVisibleAsync();

    public async Task FillCancellationReasonAsync(string reason)
    {
        await Dialog.Locator("textarea").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Clicks "Cancel Leaving Process" (the danger-styled submit button). Does not assume success
    /// — on validation failure (missing reason, client- or server-side) the dialog stays open
    /// with an inline .alert-danger (see <see cref="GetErrorAsync"/>); on success the dialog
    /// closes and the parent page force-navigates to the plain employee URL (no ?tab=, since the
    /// Leaving tab disappears once the process is Cancelled).
    /// </summary>
    public async Task ConfirmAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process" }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    /// <summary>Dismisses the dialog by clicking Close.</summary>
    public async Task CloseAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the inline error currently shown (client-side "please provide a reason", or a
    /// server-side rejection), or null if none is visible.
    /// </summary>
    public async Task<string?> GetErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
