using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Workload &amp; HR Actions report
/// (/companies/{companyId}/reporting/workload-actions — WorkloadActionsReportPage.razor).
/// Unlike the other report pages, this one has no export button, no HrGrid-backed grid columns
/// (it uses <c>HrGrid</c> per section but the same column set every time), an "Apply Filters" /
/// "Clear" button pair (not just "Apply Filters"), an optional Group By dropdown that switches
/// the page from a single flat grid to one grid per group heading (rendered as <c>&lt;h5&gt;</c>
/// elements followed by a grid), and a per-row "Go" link/button that client-side navigates via
/// <c>WorkloadActionRowModel.DeepLinkUrl</c> instead of triggering a download or opening a dialog.
/// </summary>
public sealed class WorkloadActionsReportPage(IPage page, string baseUrl)
{
    // The page renders one HrGrid per group when grouped, or a single one when flat — either way,
    // at least one grid row/emptyrow (or the "No outstanding actions" info alert) appears once
    // loading has finished.
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

    // ── Summary stat cards ─────────────────────────────────────────────────────

    private ILocator StatCard(string labelText) =>
        page.Locator(".card").Filter(new() { HasText = labelText }).First;

    public async Task<int> GetStatValueAsync(string labelText)
    {
        var text = await StatCard(labelText).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }

    // ── Filters ────────────────────────────────────────────────────────────────

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

    public Task SelectGroupByAsync(string groupByLabel) =>
        DropDownSelector.SelectAsync(page, FilterField("Group By"), groupByLabel);

    /// <summary>
    /// Sets the Due Date From/To range via the Syncfusion date picker inputs — used to force a
    /// window the seeded E2E environment's outstanding actions cannot fall inside, since the
    /// Status/Action Type dropdowns are populated only from real loaded row data and can never
    /// offer a guaranteed-nonexistent value (see WorkloadActionsReportPage.razor.LoadAsync).
    /// </summary>
    public async Task SetDueDateRangeAsync(DateOnly from, DateOnly to)
    {
        await FilterField("Due Date From").Locator("input").FillAsync(from.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Escape");
        await FilterField("Due Date To").Locator("input").FillAsync(to.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Escape");
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
    }

    // ── Grid / grouping ────────────────────────────────────────────────────────

    /// <summary>
    /// Total row count across every rendered grid (flat single grid when not grouped, or summed
    /// across all per-group grids when Group By is set) — 0 when the empty-state alert is shown
    /// instead of any grid.
    /// </summary>
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

    /// <summary>
    /// Group section headings (the <c>&lt;h5&gt;</c> elements rendered above each per-group grid
    /// when a Group By value is applied), including the trailing "(N)" item count — empty when
    /// not grouped.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetGroupHeadingsAsync()
    {
        // ApplyFiltersAsync's own wait (LoadedSelector = ".e-grid .e-row, ...") can resolve against
        // grid rows Blazor is reusing from before the click, returning before the group headings
        // for the *new* grouping have actually rendered. Wait for either a heading or the
        // empty-state alert directly so this doesn't race that re-render.
        await page.WaitForSelectorAsync("h5.mt-4, .alert-info", new() { Timeout = 15_000 });
        var headings = await page.Locator("h5.mt-4").AllAsync();
        var result = new List<string>();
        foreach (var heading in headings)
            result.Add((await heading.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-grid").First.Locator(".e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    // ── Row actions ────────────────────────────────────────────────────────────

    private ILocator GoButtons => page.Locator(".e-grid .e-row").GetByRole(AriaRole.Button, new() { Name = "Go" });

    public async Task<int> GetGoButtonCountAsync() => await GoButtons.CountAsync();

    /// <summary>
    /// Clicks the "Go" action button on the first grid row and waits for the resulting
    /// client-side navigation (via <c>Navigation.NavigateTo</c> in the page's code-behind) to
    /// leave the workload-actions report URL.
    /// </summary>
    public async Task ClickFirstRowGoButtonAsync()
    {
        await GoButtons.First.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/reporting/workload-actions"),
            new() { Timeout = 15_000 });
    }
}
