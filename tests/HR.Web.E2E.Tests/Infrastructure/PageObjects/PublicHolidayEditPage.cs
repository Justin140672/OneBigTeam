using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the public holiday create/edit page.
/// Routes: /companies/{id}/public-holidays/new  and  /companies/{id}/public-holidays/{id}
/// </summary>
public sealed class PublicHolidayEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/public-holidays/new");
        // PublicHolidayEdit has an SfDatePicker; .e-date-wrapper only appears after Blazor's
        // interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });
    }

    public async Task FillDateAsync(string ddMMyyyy)
    {
        var dateInput = page.Locator(".e-date-wrapper input.e-input").First;
        await dateInput.ClickAsync();
        await dateInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillNameAsync(string name) =>
        await page.GetByPlaceholder("e.g. Christmas Day").FillAsync(name);

    public async Task FillCountryCodeAsync(string code) =>
        await page.GetByPlaceholder("e.g. GB").FillAsync(code);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the public-holidays list on success.
        await page.WaitForURLAsync("**/public-holidays", new() { Timeout = 15_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();
}
