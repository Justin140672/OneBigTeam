using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the public holiday list page (/companies/{companyId}/public-holidays).
/// </summary>
public sealed class PublicHolidayListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/public-holidays");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ClickNewPublicHolidayAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/public-holidays/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasHolidayAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetHolidayNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
        {
            var text = (await cell.TextContentAsync())?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
                names.Add(text);
        }
        return names;
    }

    /// <summary>Applies a year filter by filling the year numeric input and clicking Apply.</summary>
    public async Task FilterByYearAsync(int year)
    {
        // Syncfusion puts class e-numerictextbox ON the <input> itself, not on a wrapper.
        var yearInput = page.Locator("input.e-numerictextbox").First;
        await yearInput.FillAsync(year.ToString());
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
