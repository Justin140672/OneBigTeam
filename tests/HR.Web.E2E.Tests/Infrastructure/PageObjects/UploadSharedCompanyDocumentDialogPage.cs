using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class UploadSharedCompanyDocumentDialogPage(IPage page, string baseUrl)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });

    public async Task GoToListAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents");
        await page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
    }

    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public async Task FillTitleAsync(string title)
    {
        await Dialog.GetByPlaceholder("Document title").FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" }), categoryLabel);

    public Task SetFileAsync(string filePath) =>
        Dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    private ILocator RequiresAcknowledgementCheckboxWrapper =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "Requires employee acknowledgement" });

    public Task<bool> IsRequiresAcknowledgementCheckedAsync() =>
        RequiresAcknowledgementCheckboxWrapper.Locator("input[type='checkbox']").IsCheckedAsync();

    public async Task CheckRequiresAcknowledgementAsync()
    {
        await RequiresAcknowledgementCheckboxWrapper.Locator("label").ClickAsync();
        var textArea = Dialog.Locator("textarea").First;
        await textArea.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        try
        {
            await page.WaitForFunctionAsync(
                "el => el.value.trim().length > 0",
                await textArea.ElementHandleAsync(),
                new PageWaitForFunctionOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    private ILocator AcknowledgementDueDateInput =>
        Dialog.Locator(".col-md-6").Filter(new() { HasText = "Acknowledgement Due Date" }).Locator(".e-date-wrapper input.e-input");

    public async Task FillAcknowledgementDueDateAsync(DateOnly dueDate)
    {
        await AcknowledgementDueDateInput.ClickAsync();
        await AcknowledgementDueDateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");
    }

    private ILocator AcknowledgementStatementTextArea =>
        Dialog.Locator(".col-12").Filter(new() { HasText = "Acknowledgement Statement" }).Locator("textarea");

    public Task<string> GetAcknowledgementStatementValueAsync() =>
        AcknowledgementStatementTextArea.InputValueAsync();

    public async Task FillAcknowledgementStatementAsync(string value)
    {
        await AcknowledgementStatementTextArea.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetAcknowledgementPreviewTextAsync() =>
        (await Dialog.Locator(".alert-info").InnerTextAsync()).Trim();

    public async Task<string?> GetAcknowledgementStatementValidationErrorAsync()
    {
        var error = Dialog.Locator(".text-danger.small").Filter(new() { HasText = "acknowledgement statement is required" });
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    public async Task<string?> GetGlobalErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger");
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    public Task ClickUploadAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

    public async Task UploadAndWaitForCloseAsync()
    {
        await ClickUploadAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        await page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
        var href = await page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        return Guid.Parse(href!.Split('/').Last());
    }
}
