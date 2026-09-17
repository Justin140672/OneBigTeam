using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Waive Obligation" dialog (WaiveOffboardingTaskDialog.razor), opened via
/// the "Waive" action button on an in-progress checklist row within the unified "Leaving &amp;
/// Offboarding" workspace (see <see cref="EmployeeOffboardingTab.ClickWaiveAsync"/>) — only shown
/// to HR administrators (CanWaive) while the leaving process is InProgress and the obligation's
/// Status is Pending/InProgress. Requires a free-text Waiver Reason.
///
/// Follows the standalone-page-object-per-dialog pattern established by
/// <see cref="StartLeavingProcessDialog"/> / <see cref="CancelLeavingProcessDialog"/>.
/// </summary>
public sealed class WaiveOffboardingTaskDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Waive Obligation" });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task FillReasonAsync(string reason)
    {
        await Dialog.Locator("textarea").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Clicks "Waive Obligation" (the warning-styled submit button). Does not assume success — on
    /// validation failure (missing reason) the dialog stays open with an inline .alert-danger (see
    /// <see cref="GetErrorAsync"/>); on success the dialog closes and the parent checklist refreshes.
    /// </summary>
    public async Task ConfirmAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Waive Obligation" }).ClickAsync();

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
