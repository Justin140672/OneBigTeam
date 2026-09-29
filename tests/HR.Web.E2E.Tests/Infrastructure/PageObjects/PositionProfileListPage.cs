using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class PositionProfileListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles");
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 30_000 });
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger",
            new() { Timeout = 30_000 });
    }

    public async Task ClickNewPositionProfileAsync()
    {
        await page.ClickGridAddAndWaitForCreateRouteAsync("**/position-profiles/new**");
    }

    public Task<bool> HasPositionProfileAsync(string titleFragment) =>
        page.HasGridCellOnAnyPageAsync(titleFragment);

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
        if (!await page.HasGridCellOnAnyPageAsync(title))
            throw new InvalidOperationException($"Position profile '{title}' was not found on any page of the list.");

        await page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.ClickAsync();
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task DeactivateAsync(string title)
    {
        if (!await page.HasGridCellOnAnyPageAsync(title))
            throw new InvalidOperationException($"Position profile '{title}' was not found on any page of the list.");

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string title)
    {
        if (!await page.HasGridCellOnAnyPageAsync(title))
            return false;

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = title })
            .First;
        var badge = row.Locator(".status-badge.status-badge--success");
        return await badge.IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }
}
