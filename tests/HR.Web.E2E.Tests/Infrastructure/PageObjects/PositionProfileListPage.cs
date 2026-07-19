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

    public async Task OpenPositionProfileAsync(string title)
    {
        await page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.ClickAsync();
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Deactivates the position profile whose row contains <paramref name="title"/>
    /// by clicking the deactivate toolbar action.
    /// </summary>
    public async Task DeactivateAsync(string title)
    {
        // Select the row first, then click the deactivate toolbar button.
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        await row.ClickAsync();
        // Blazor re-renders the toolbar after row selection; wait for the button to be enabled
        // (same pattern as DepartmentListPage.DeactivateDepartmentAsync).
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        // Wait for the grid to refresh.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsActiveAsync(string title)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        var badge = row.Locator(".status-badge.status-badge--success");
        return await badge.IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
