using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location type create/edit page.
/// Routes: /companies/{id}/location-types/new  and  /companies/{id}/location-types/{id}
/// </summary>
// CS9113 (baseUrl unread): kept for constructor-signature consistency with the other page objects
// in this suite, all of which take (IPage page, string baseUrl) even where — as here — the page
// has no direct-navigation helper yet.
#pragma warning disable CS9113
public sealed class LocationTypeEditPage(IPage page, string baseUrl)
#pragma warning restore CS9113
{
    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Office, Warehouse").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetNameAsync() =>
        page.GetByPlaceholder("e.g. Office, Warehouse").InputValueAsync();

    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the location-types list on success.
        await page.WaitForURLAsync("**/location-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
