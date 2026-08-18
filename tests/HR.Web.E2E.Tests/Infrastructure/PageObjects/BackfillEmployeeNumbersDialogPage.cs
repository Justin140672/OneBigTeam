using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Backfill Employee Numbers" dialog
/// (BackfillEmployeeNumbersDialog.razor), opened from the "Employee Numbering" section of the
/// Company Settings tab (see <see cref="CompanyEditPage.OpenBackfillEmployeeNumbersDialogAsync"/>)
/// while the company's employee-numbering mode is Automatic.
/// </summary>
public sealed class BackfillEmployeeNumbersDialogPage(IPage page, string baseUrl)
{
    private ILocator Dialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Backfill Employee Numbers" });

    /// <summary>
    /// Waits for the dialog to be visible and its preview to finish loading (the dialog shows a
    /// loading indicator while the preview GET call is in flight — see
    /// BackfillEmployeeNumbersDialog.razor's `_loadingPreview` field).
    /// </summary>
    public async Task WaitForPreviewLoadedAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        // HrLoadingIndicator (.hr-loading > .spinner-border) is shown while the preview GET is in
        // flight and removed from the DOM once it resolves — wait for it to disappear rather than
        // just checking visibility, since Blazor removes the whole element (no offsetParent check
        // needed, but harmless to include for parity with other dialogs' wait patterns).
        await page.WaitForSelectorAsync(
            ".backfill-employee-numbers-dialog .hr-loading",
            new() { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>The "There are no employees missing an employee number." empty-state message.</summary>
    public Task<bool> HasEmptyStateMessageAsync() =>
        Dialog.GetByText("There are no employees missing an employee number.").IsVisibleAsync();

    /// <summary>
    /// The "N employee(s) are missing an employee number…" summary paragraph shown above the
    /// candidates grid when there's at least one candidate.
    /// </summary>
    public Task<bool> HasCandidateSummaryTextAsync() =>
        Dialog.GetByText("are missing an employee number and will be").IsVisibleAsync();

    /// <summary>Any global/API error banner shown in place of the preview content.</summary>
    public Task<bool> HasGlobalErrorAsync() =>
        Dialog.Locator(".alert-danger").IsVisibleAsync();

    public async Task<string?> GetGlobalErrorTextAsync() =>
        await Dialog.Locator(".alert-danger").IsVisibleAsync()
            ? (await Dialog.Locator(".alert-danger").TextContentAsync())?.Trim()
            : null;

    /// <summary>Number of rows currently rendered in the candidates grid.</summary>
    public Task<int> GetCandidateRowCountAsync() =>
        Dialog.Locator(".e-grid .e-row").CountAsync();

    /// <summary>
    /// Returns the "Predicted Employee Number" cell text for the row matching the given last-name
    /// fragment.
    /// </summary>
    public async Task<string?> GetPredictedEmployeeNumberForAsync(string lastNameFragment)
    {
        var row = Dialog.Locator(".e-row").Filter(new() { HasText = lastNameFragment }).First;
        try
        {
            await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await row.Locator(".e-rowcell").Last.TextContentAsync())?.Trim();
    }

    private ILocator ConfirmButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm Backfill" });

    public Task<bool> IsConfirmButtonVisibleAsync() => ConfirmButton.IsVisibleAsync();

    public async Task<bool> IsConfirmButtonEnabledAsync() =>
        await ConfirmButton.IsVisibleAsync() && await ConfirmButton.IsEnabledAsync();

    public async Task ConfirmAsync()
    {
        await ConfirmButton.ClickAsync();
        // The dialog swaps to a success banner once CommitAsync resolves; wait for either the
        // success text or a global error to appear rather than a fixed delay.
        await page.WaitForFunctionAsync(
            "document.querySelector('.backfill-employee-numbers-dialog .alert-success, " +
            ".backfill-employee-numbers-dialog .alert-danger') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>The success summary banner ("Successfully assigned employee numbers to N employee(s).").</summary>
    public Task<bool> HasSuccessSummaryAsync() =>
        Dialog.Locator(".alert-success").IsVisibleAsync();

    public async Task<string?> GetSuccessSummaryTextAsync() =>
        await Dialog.Locator(".alert-success").IsVisibleAsync()
            ? (await Dialog.Locator(".alert-success").TextContentAsync())?.Trim()
            : null;

    public async Task CloseAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }
}
