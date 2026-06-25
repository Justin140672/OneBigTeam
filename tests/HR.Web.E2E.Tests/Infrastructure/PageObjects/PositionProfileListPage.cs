using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the position profile list page (/companies/{companyId}/position-profiles).
/// </summary>
public sealed class PositionProfileListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ClickNewPositionProfileAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/position-profiles/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasPositionProfileAsync(string titleFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = titleFragment })
            .First
            .IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetPositionProfileTitlesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var titles = new List<string>();
        foreach (var cell in cells)
            titles.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return titles;
    }
}
