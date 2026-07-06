using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy list page (/companies/{companyId}/vacancies).
/// </summary>
public sealed class VacancyListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies");
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ClickNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasVacancyAsync(string titleFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = titleFragment })
            .First
            .IsVisibleAsync();

    public async Task ClickVacancyAsync(string titleFragment)
    {
        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = titleFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/vacancies/**", new() { Timeout = 15_000 });
    }
}
