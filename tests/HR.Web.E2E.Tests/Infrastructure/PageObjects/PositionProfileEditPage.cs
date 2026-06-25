using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the position profile create/edit page.
/// Routes: /companies/{id}/position-profiles/new  and  /companies/{id}/position-profiles/{id}
/// </summary>
public sealed class PositionProfileEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/new");
        // PositionProfileEdit has an SfDropDownList for Department; span[role='combobox'] only
        // appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid positionProfileId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/{positionProfileId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillTitleAsync(string title) =>
        await page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(title);

    public async Task FillDescriptionAsync(string description) =>
        await page.GetByPlaceholder("Optional description").FillAsync(description);

    public async Task SetManagerialRoleAsync(bool isManagerial)
    {
        var checkbox = page.GetByLabel("Managerial role");
        var isChecked = await checkbox.IsCheckedAsync();
        if (isManagerial && !isChecked) await checkbox.CheckAsync();
        if (!isManagerial && isChecked) await checkbox.UncheckAsync();
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the position-profiles list on success.
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 15_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();
}
