using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class WorkloadActionsReportPage(IPage page, string baseUrl)
{
    private const string LoadedSelector =
        ".e-grid .e-row, .e-grid .e-emptyrow, .alert-info";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/workload-actions");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.Locator(".alert-info", new() { HasText = "No outstanding actions. Everything is up to date." })
            .IsVisibleAsync();


    private ILocator StatCard(string labelText) =>
        page.Locator(".card").Filter(new() { HasText = labelText }).First;

    public async Task<int> GetStatValueAsync(string labelText)
    {
        var text = await StatCard(labelText).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }


    private ILocator FilterField(string labelText) => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = labelText }).First;

    public Task SelectActionTypeAsync(string actionType) =>
        DropDownSelector.SelectAsync(page, FilterField("Action Type"), actionType);

    public Task SelectDepartmentAsync(string department) =>
        DropDownSelector.SelectAsync(page, FilterField("Department"), department);

    public Task SelectUrgencyAsync(string urgencyLabel) =>
        DropDownSelector.SelectAsync(page, FilterField("Urgency"), urgencyLabel);

    public Task SelectStatusAsync(string status) =>
        DropDownSelector.SelectAsync(page, FilterField("Status"), status);

    public Task SelectEmployeeAsync(string employeeName) =>
        DropDownSelector.SelectAsync(page, FilterField("Employee"), employeeName);

    public async Task SelectGroupByAsync(string groupByLabel)
    {
        try
        {
            await DropDownSelector.SelectAsync(page, FilterField("Group By"), groupByLabel);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
            await Assertions.Expect(FilterField("Group By").Locator("span[role='combobox'] input").First)
                .ToHaveValueAsync(groupByLabel, new() { Timeout = 15_000 });
        }
    }

    public async Task SetDueDateRangeAsync(DateOnly from, DateOnly to)
    {
        await FilterField("Due Date From").Locator("input").FillAsync(from.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Escape");
        await FilterField("Due Date To").Locator("input").FillAsync(to.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Escape");
    }

    private ILocator LoadRoot => page.Locator("[data-load-version]").First;

    private async Task ClickAndWaitForReloadAsync(ILocator button)
    {
        var versionBefore = await LoadRoot.GetAttributeAsync("data-load-version");
        await button.ClickAsync();
        await Assertions.Expect(LoadRoot).Not.ToHaveAttributeAsync("data-load-version", versionBefore ?? string.Empty, new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
    }

    public Task WaitForLoadedAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    public Task ApplyFiltersAsync() =>
        ClickAndWaitForReloadAsync(page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }));

    public Task ClearFiltersAsync() =>
        ClickAndWaitForReloadAsync(page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }));


    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        if (await IsEmptyStateVisibleAsync())
            return 0;

        var grids = page.Locator(".e-grid");
        var count = 0;
        var gridCount = await grids.CountAsync();
        for (var i = 0; i < gridCount; i++)
        {
            var grid = grids.Nth(i);
            if (await grid.Locator(".e-emptyrow").CountAsync() > 0)
                continue;
            count += await grid.Locator(".e-row").CountAsync();
        }
        return count;
    }

    public async Task<IReadOnlyList<string>> GetGroupHeadingsAsync()
    {
        var emptyState = page.Locator(".alert-info", new() { HasText = "No outstanding actions. Everything is up to date." });
        await page.Locator("h5.mt-4").Or(emptyState).First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var headings = await page.Locator("h5.mt-4").AllTextContentsAsync();
        return headings.Select(h => h.Trim()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-grid").First.Locator(".e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }


    private ILocator GoButtons => page.Locator(".e-grid .e-row").GetByRole(AriaRole.Button, new() { Name = "Go" });

    public async Task<int> GetGoButtonCountAsync() => await GoButtons.CountAsync();

    public Task ClickFirstRowGoButtonAsync() => GoButtons.First.ClickAsync();
}
