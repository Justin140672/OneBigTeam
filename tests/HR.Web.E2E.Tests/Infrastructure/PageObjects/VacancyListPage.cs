using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy list page (/companies/{companyId}/vacancies).
/// </summary>
public sealed class VacancyListPage(IPage page, string baseUrl)
{
    // Waiting for ".e-grid" alone (or for the Blazor-side loading spinner to clear) is NOT
    // sufficient to guarantee rows are queryable: Syncfusion's EJ2 grid does its own JS render
    // pass to populate ".e-row"/".e-rowcell" into the DOM on a separate tick after the Blazor
    // component itself has mounted. Waiting for the row selector (or its empty-state sibling)
    // directly is the only wait that's actually tied to the data being present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies/new", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Waits for the grid's rows to actually be rendered, then checks whether a row with this
    /// title is present. Callers that navigate here via something other than GoToAsync (e.g.
    /// clicking a dashboard widget) won't have already waited for this, so checking immediately
    /// on arrival can race the load and report false negatives while rows are still populating.
    /// </summary>
    public async Task<bool> HasVacancyAsync(string titleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = titleFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task ClickVacancyAsync(string titleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = titleFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/vacancies/**", new() { Timeout = 15_000 });

        // The URL changes as soon as client-side routing kicks in — well before the vacancy detail
        // page's own async load (_vacancy, Linked Position Profile card, etc.) has actually
        // finished rendering. A caller that immediately asserts on that page's content right after
        // this method returns can otherwise race the load (same reasoning as VacancyDetailPage.
        // GoToAsync's own post-navigation wait).
        await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the text of the given 0-based column index for the row matching
    /// <paramref name="titleFragment"/>. Column order matches VacancyList.razor's GridColumns:
    /// 0=Title, 1=Position Profile, 2=Location, 3=Status, 4=Opened, 5=Closed.
    /// </summary>
    public async Task<string> GetRowCellAsync(string titleFragment, int columnIndex)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row").Filter(new() { HasText = titleFragment }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Reads the "Position Profile" column's text (the linked profile's own Title, or blank for
    /// the rare legacy vacancy with no linked profile) for the row matching
    /// <paramref name="titleFragment"/> — see VacancyListItemModel.PositionProfileTitle and the
    /// "Derive Vacancy Role Information from Position Profile" story's new grid column.
    /// </summary>
    public Task<string> GetPositionProfileColumnTextAsync(string titleFragment) =>
        GetRowCellAsync(titleFragment, columnIndex: 1);

    /// <summary>
    /// Returns true if the Title column's "(from Position Profile)" muted-italic fallback
    /// indicator (rendered only when AdvertTitle is null — see VacancyList.razor's Title
    /// GridColumn Template) is present within the row matching <paramref name="titleFragment"/>.
    /// Scoped to the Title column's own cell (column 0) so it can't accidentally match the
    /// Location column's identical suffix text on the same row.
    /// </summary>
    public async Task<bool> HasTitleColumnPositionProfileFallbackIndicatorAsync(string titleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        // Scoped to the Title cell (column 0) specifically, not "row contains this text anywhere"
        // — the Position Profile column (column 1) can independently contain the same text as
        // titleFragment (e.g. a vacancy with its own AdvertTitle override that's still linked to
        // the same Position Profile named titleFragment), which would otherwise make a whole-row
        // HasText filter match the wrong row.
        var row = page.Locator(".e-row")
            .Filter(new() { Has = page.Locator(".e-rowcell:first-child", new() { HasText = titleFragment }) })
            .First;
        var titleCell = row.Locator(".e-rowcell").Nth(0);
        return await titleCell.Locator("span.fst-italic", new() { HasText = "(from Position Profile)" }).IsVisibleAsync();
    }

    // NOTE: the Location column (VacancyList.razor's EffectiveLocation GridColumn) has no
    // override-vs-fallback distinction to indicate anymore — Vacancy.Location was removed
    // entirely as part of the "Vacancy - Position Profile relationship" epic's location
    // correction, so Location is now unconditionally just the linked Position Profile's location,
    // rendered as a plain field with no Template/indicator. A
    // HasLocationColumnPositionProfileFallbackIndicatorAsync method used to live here targeting
    // a fallback indicator that no longer exists in the markup; it was removed along with the
    // test that exercised it.

    // ── Position Profile / Department filters ────────────────────────────────────
    // "Vacancy - Position Profile relationship" epic: two new server-side filter dropdowns
    // (SfDropDownList, AllowFiltering/ShowClearButton) above the grid, reloading the list via
    // SearchPageBase.LoadAsync() on ValueChange (see VacancyList.razor). Neither dropdown was
    // given an explicit data-testid, so these are scoped by their ".col-md-3" wrapper's label
    // text, matching the pattern already used for the detail page's Hiring Manager/Position
    // Profile dropdowns (VacancyDetailPage.SelectHiringManagerAsync).

    private ILocator PositionProfileFilterGroup =>
        page.Locator(".col-md-3").Filter(new() { HasText = "Position Profile" }).First;

    private ILocator DepartmentFilterGroup =>
        page.Locator(".col-md-3").Filter(new() { HasText = "Department" }).First;

    private async Task SelectFilterAsync(ILocator group, string optionText)
    {
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(optionText);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = optionText })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Selects a value from the "Position Profile" filter dropdown above the grid.</summary>
    public Task SelectPositionProfileFilterAsync(string titleFragment) =>
        SelectFilterAsync(PositionProfileFilterGroup, titleFragment);

    /// <summary>Selects a value from the "Department" filter dropdown above the grid.</summary>
    public Task SelectDepartmentFilterAsync(string nameFragment) =>
        SelectFilterAsync(DepartmentFilterGroup, nameFragment);

    /// <summary>Clicks the "Position Profile" filter's clear (X) icon (ShowClearButton="true") and waits for the grid to reload.</summary>
    public async Task ClearPositionProfileFilterAsync()
    {
        await PositionProfileFilterGroup.Locator(".e-clear-icon").ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Clicks the "Department" filter's clear (X) icon (ShowClearButton="true") and waits for the grid to reload.</summary>
    public async Task ClearDepartmentFilterAsync()
    {
        await DepartmentFilterGroup.Locator(".e-clear-icon").ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Reads the current input text of the "Position Profile" filter dropdown.</summary>
    public Task<string> GetPositionProfileFilterTextAsync() =>
        PositionProfileFilterGroup.Locator(".e-input-group input").First.InputValueAsync();

    /// <summary>Reads the current input text of the "Department" filter dropdown.</summary>
    public Task<string> GetDepartmentFilterTextAsync() =>
        DepartmentFilterGroup.Locator(".e-input-group input").First.InputValueAsync();

    /// <summary>
    /// Returns the number of rows currently rendered in the grid (excluding the empty-row
    /// placeholder), for asserting a filter narrowed (or a cleared filter widened) the result set.
    /// </summary>
    public async Task<int> GetVisibleRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-grid .e-row:not(.e-emptyrow)").CountAsync();
    }
}
