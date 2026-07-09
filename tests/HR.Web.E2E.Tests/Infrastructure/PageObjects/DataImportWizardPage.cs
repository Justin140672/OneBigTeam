using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee data import wizard: upload -> validate -> preview -> confirm.
/// </summary>
public sealed class DataImportWizardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/data-import/employees");
        await page.WaitForSelectorAsync("input[type='file']", new() { Timeout = 20_000 });
    }

    public async Task UploadFileAsync(string filePath)
    {
        var fileInput = page.Locator("input[type='file']");
        await fileInput.SetInputFilesAsync(filePath);

        await page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        // The "Validate" section only renders once the upload response has set the session.
        await page.GetByRole(AriaRole.Button, new() { Name = "Validate", Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ValidateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Validate", Exact = true }).ClickAsync();

        // "View Preview" button only appears once the validate response has landed.
        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ViewPreviewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" }).ClickAsync();

        // The Preview card header only renders once preview data has loaded.
        await page.GetByText("3. Preview").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<bool> HasValidRowAsync(string workEmailFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = workEmailFragment })
            .First
            .IsVisibleAsync();

    public async Task ConfirmImportAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Import" }).ClickAsync();

        // The Result card header only renders once the confirm response has landed.
        await page.GetByText("4. Result").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string> GetResultStatusAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "4. Result" });
        var statusDd = resultCard.Locator("dd").First;
        return await statusDd.InnerTextAsync();
    }

    public async Task<int> GetCreatedCountAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "4. Result" });
        var createdDd = resultCard.Locator("dd").Nth(1);
        return int.Parse(await createdDd.InnerTextAsync());
    }
}
