using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Backfill Employee Timeline" dialog
/// (BackfillEmployeeTimelineDialog.razor), opened from the "Employee Timeline" subsection of the
/// Company Settings tab (see <see cref="CompanyEditPage.OpenBackfillEmployeeTimelineDialogAsync"/>).
/// Unlike <see cref="BackfillEmployeeNumbersDialogPage"/>, this dialog has no preview step — it is
/// confirmation-only, so there is no candidates grid or empty-state message to wait for.
/// </summary>
public sealed class BackfillEmployeeTimelineDialogPage(IPage page, string baseUrl)
{
    private ILocator Dialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Backfill Employee Timeline" });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task WaitForVisibleAsync() =>
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

    /// <summary>
    /// The confirmation text shown before the backfill has run:
    /// "This will generate historical timeline entries from existing employee data. This may take a
    /// moment and can be safely re-run without creating duplicates."
    /// </summary>
    public Task<bool> HasConfirmationTextAsync() =>
        Dialog.GetByText("This will generate historical timeline entries from existing employee data").IsVisibleAsync();

    private ILocator ConfirmButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm Backfill" });

    public Task<bool> IsConfirmButtonVisibleAsync() => ConfirmButton.IsVisibleAsync();

    public async Task<bool> IsConfirmButtonEnabledAsync() =>
        await ConfirmButton.IsVisibleAsync() && await ConfirmButton.IsEnabledAsync();

    private ILocator CancelButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public Task<bool> IsCancelButtonVisibleAsync() => CancelButton.IsVisibleAsync();

    public async Task ConfirmAsync()
    {
        await ConfirmButton.ClickAsync();
        // The dialog swaps its content to a success/error banner once CommitBackfillAsync resolves;
        // wait for either to appear rather than a fixed delay.
        await page.WaitForFunctionAsync(
            "document.querySelector('.backfill-employee-timeline-dialog .alert-success, " +
            ".backfill-employee-timeline-dialog .alert-danger') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// The success summary banner ("Timeline backfill complete: N created, N skipped, N failed."),
    /// shown alongside a per-source breakdown table (Source/Created/Skipped/Failed).
    /// </summary>
    public Task<bool> HasSuccessSummaryAsync() =>
        Dialog.Locator(".alert-success").IsVisibleAsync();

    public async Task<string?> GetSuccessSummaryTextAsync() =>
        await Dialog.Locator(".alert-success").IsVisibleAsync()
            ? (await Dialog.Locator(".alert-success").TextContentAsync())?.Trim()
            : null;

    /// <summary>Number of data rows in the per-source breakdown table shown after a successful commit.</summary>
    public Task<int> GetBreakdownRowCountAsync() =>
        Dialog.Locator("table.table-sm tbody tr").CountAsync();

    public Task<bool> HasGlobalErrorAsync() =>
        Dialog.Locator(".alert-danger").IsVisibleAsync();

    public async Task<string?> GetGlobalErrorTextAsync() =>
        await Dialog.Locator(".alert-danger").IsVisibleAsync()
            ? (await Dialog.Locator(".alert-danger").TextContentAsync())?.Trim()
            : null;

    public async Task CloseAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }
}
