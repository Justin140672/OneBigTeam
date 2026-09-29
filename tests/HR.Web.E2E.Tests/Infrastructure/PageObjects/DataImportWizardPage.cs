using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

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

        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string> GetMappingSelectionAsync(string standardHeaderName)
    {
        var mappingCard = page.Locator(".card", new() { HasText = "2. Column Mapping" });

        var row = mappingCard.Locator("tr")
            .Filter(new() { Has = page.Locator($"td:text-is(\"{standardHeaderName}\")") })
            .First;
        var combobox = row.Locator("span[role='combobox']").First;
        var input = combobox.Locator("input").First;

        string value = "";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            value = (await input.InputValueAsync()).Trim();
            if (!string.IsNullOrEmpty(value)) break;
            await page.WaitForTimeoutAsync(250);
        }

        return value;
    }

    public async Task ContinueFromMappingAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ViewPreviewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" }).ClickAsync();

        await page.GetByRole(AriaRole.Heading, new() { Name = "4. Preview & Confirm" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<bool> HasValidRowAsync(string workEmailFragment)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = workEmailFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task ConfirmImportAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Import" }).ClickAsync();

        await page.GetByText("5. Completion Summary").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string> GetResultStatusAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "5. Completion Summary" });
        var statusDd = resultCard.Locator("dd").First;
        return await statusDd.InnerTextAsync();
    }

    public async Task<int> GetCreatedCountAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "5. Completion Summary" });
        var createdDd = resultCard.Locator("dd").Nth(1);
        return int.Parse(await createdDd.InnerTextAsync());
    }

    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Template" }).ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    public async Task<string> ClickDownloadErrorReportAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Error Report" }).First.ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }
}
