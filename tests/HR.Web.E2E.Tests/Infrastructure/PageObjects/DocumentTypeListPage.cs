using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the document type list page (/companies/{companyId}/document-types).
/// </summary>
public sealed class DocumentTypeListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/document-types");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/document-types/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasItemAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();
}
