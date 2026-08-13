using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Import" dialog opened from the Employee List's "Bulk Update" toolbar
/// dropdown (Components/Pages/Employees/BulkCompensationImportDialog.razor), which wraps
/// BulkCompensationImportPanel.razor in an SfDialog.
///
/// All locators are scoped to the dialog element itself (identified by its own CssClass,
/// "bulk-compensation-import-dialog", combined with role='dialog') so they can't collide with
/// same-named controls elsewhere on the page.
/// </summary>
public sealed class BulkCompensationImportDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-compensation-import-dialog");

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public Task UploadImportFileAsync(string filePath) =>
        Dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    public async Task ClickImportAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Import from Excel", Exact = true }).ClickAsync();

        // Either the dialog closes (a successful import bubbles OnImported -> EmployeeList, which
        // closes the dialog and shows its own top-level success banner) or the panel's own nested
        // row-errors/global-error alert appears while the dialog stays open. These need to be
        // raced as two genuinely different outcomes, not folded into one WaitForSelectorAsync
        // call: the dialog root itself is already Attached the whole time (we're clicking inside
        // it), so a State=Attached wait against ".alert-danger, [dialog selector]" resolves on the
        // always-already-satisfied second alternative immediately, never actually waiting for the
        // error banner to render — which is what made the follow-up GetRowErrorsTextAsync() call
        // race the real async import result instead of observing it.
        var errorTask = Dialog.Locator(".alert-danger").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var closedTask = Dialog.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await Task.WhenAny(errorTask, closedTask);
    }

    public async Task<string?> GetRowErrorsTextAsync()
    {
        var banner = Dialog.Locator(".alert-danger");
        return await banner.WaitUntilVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public Task ClickCloseAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
}
