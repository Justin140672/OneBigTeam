using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the admin employee profile page
/// (/companies/{id}/employees/{employeeId}).
/// Provides access to the Documents and Working Pattern sections.
/// </summary>
public sealed class EmployeeAdminPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        // EmployeeEdit has SfDropDownList components on the Details tab; span[role='combobox']
        // only appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── Documents tab ─────────────────────────────────────────────────────────

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents" }).ClickAsync();
        // Spinner appears while loading, then grid renders
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        // Wait for Syncfusion grid rows to be in the DOM (not just the card shell)
        await page.WaitForSelectorAsync(".e-gridcontent td, .card-body td", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if any grid cell in the Documents tab contains <paramref name="titleFragment"/>.</summary>
    public async Task<bool> HasDocumentAsync(string titleFragment) =>
        await page.Locator(".e-gridcontent td, .card-body td")
            .Filter(new() { HasText = titleFragment })
            .First
            .IsVisibleAsync();

    // ── Working Pattern section (Details tab) ─────────────────────────────────

    /// <summary>Ensures the "Override company defaults" checkbox is checked.</summary>
    public async Task EnableWorkingPatternOverrideAsync()
    {
        // Syncfusion puts class e-numerictextbox ON the <input> itself; the Hours Per Day
        // field is only rendered while OverrideWorkingPattern is true. If a previous test
        // run already saved the override, the input is already visible — don't click again
        // or we would toggle it off.
        var numericInput = page.Locator("input.e-numerictextbox");
        if (!await numericInput.IsVisibleAsync())
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Override company defaults" });
            await wrapper.Locator("label").ClickAsync();
            await numericInput.WaitForAsync(new() { Timeout = 10_000 });
        }
    }

    /// <summary>
    /// Sets the Hours Per Day numeric field in the Working Pattern section.
    /// Assumes the override is already enabled.
    /// </summary>
    public async Task SetHoursPerDayAsync(decimal hours)
    {
        var input = page.Locator("input.e-numerictextbox").First;
        await input.FillAsync(hours.ToString("0.#"));
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates to /employees list on success
        await page.WaitForURLAsync("**/employees", new() { Timeout = 15_000 });
    }
}
