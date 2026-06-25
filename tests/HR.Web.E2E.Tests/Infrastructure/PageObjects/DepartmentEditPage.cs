using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the department create/edit page.
/// Routes: /companies/{id}/departments/new  and  /companies/{id}/departments/{id}
/// </summary>
public sealed class DepartmentEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/departments/new");
        // DepartmentEdit has an SfDropDownList for Parent Department; span[role='combobox'] only
        // appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid departmentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/departments/{departmentId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name) =>
        await page.GetByPlaceholder("Department name").FillAsync(name);

    public async Task FillDescriptionAsync(string description) =>
        await page.GetByPlaceholder("Optional description").FillAsync(description);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the department list on success.
        await page.WaitForURLAsync("**/departments", new() { Timeout = 15_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();
}
