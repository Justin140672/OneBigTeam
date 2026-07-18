using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location type create/edit page.
/// Routes: /companies/{id}/location-types/new  and  /companies/{id}/location-types/{id}
/// </summary>
public sealed class LocationTypeEditPage(IPage page, string baseUrl)
{
    public Task FillNameAsync(string name) =>
        page.GetByPlaceholder("e.g. Office, Warehouse").FillAsync(name);

    public Task<string> GetNameAsync() =>
        page.GetByPlaceholder("e.g. Office, Warehouse").InputValueAsync();

    public Task FillDescriptionAsync(string description) =>
        page.GetByPlaceholder("Optional description").FillAsync(description);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the location-types list on success.
        await page.WaitForURLAsync("**/location-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
