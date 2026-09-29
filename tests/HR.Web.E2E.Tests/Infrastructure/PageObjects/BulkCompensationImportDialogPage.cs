using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class BulkCompensationImportDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-compensation-import-dialog");

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public Task UploadImportFileAsync(string filePath) =>
        Dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    public async Task ClickImportAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Import from Excel", Exact = true }).ClickAsync();

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
