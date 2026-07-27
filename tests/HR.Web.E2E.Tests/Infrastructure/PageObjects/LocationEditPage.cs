using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location create/edit page.
/// Routes: /companies/{id}/locations/new  and  /companies/{id}/locations/{id}
/// </summary>
public sealed class LocationEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/locations/new");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid locationId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/locations/{locationId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name) =>
        await page.GetByPlaceholder("Location name").FillAsync(name);

    public async Task FillDescriptionAsync(string description) =>
        await page.GetByPlaceholder("Optional description").FillAsync(description);

    /// <summary>
    /// Selects a location type from the Syncfusion dropdown by filtering on a fragment of its
    /// name. Follows the popup-hidden-wait + committed-value-assertion pattern used elsewhere
    /// for Syncfusion dropdowns (see VacancyDetailPage.SelectPositionProfileAsync) — popup-hidden
    /// alone can be a purely client-side JS close animation and isn't proof that Blazor's
    /// ValueChanged round-trip to the server actually committed the selected id yet.
    /// </summary>
    public async Task SelectLocationTypeAsync(string nameFragment)
    {
        var group = page.Locator(".mb-3").Filter(new() { HasText = "Location Type" }).First;
        await DropDownSelector.SelectAsync(page, group, nameFragment);

        await Assertions.Expect(group.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(nameFragment)), new() { Timeout = 10_000 });
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/locations", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message, .invalid-feedback").First.IsVisibleAsync();

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("Location name").InputValueAsync();
}
