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

    /// <summary>
    /// The standard SearchPageBase "Show Inactive"/"Show Active" toolbar toggle (SupportsActiveFilter)
    /// — defaults to hiding Closed vacancies from the list until toggled; see VacancyList.razor's
    /// ShowInactive/DisplayedItems.
    /// </summary>
    public Task<bool> IsShowingActiveOnlyAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).IsVisibleAsync();

    /// <summary>
    /// Clicks "Show Inactive" so closed vacancies are shown too. Waits for the toolbar toggle to
    /// flip to "Show Active" rather than for <see cref="RowsRenderedSelector"/> — the grid already
    /// has rows rendered from before the click (DisplayedItems is a client-side filter over
    /// already-loaded Items, not a server round-trip), so that selector is satisfied instantly and
    /// doesn't prove the re-filtered (now-including-closed) grid has actually re-rendered.
    /// </summary>
    public async Task ShowAllVacanciesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Active" }).WaitForAsync(
            new() { Timeout = 15_000 });
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

    public async Task SearchAsync(string query)
    {
        // VacancyList.razor's search placeholder is "Search by title or position profile".
        var searchInput = page.GetByPlaceholder("Search by title or position profile");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change, not on the "input" event
        // Playwright's FillAsync dispatches — without an explicit Enter/blur here,
        // SearchPageBase.OnSearchChanged never actually fires and the grid silently keeps showing
        // the unfiltered rows (same reasoning as EmployeeListPage.SearchAsync).
        await searchInput.PressAsync("Enter");
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickVacancyAsync(string titleFragment)
    {
        // The list endpoint has no pagination (VacancyList.razor's FetchItemsAsync loads every
        // vacancy for the company on every call) and GridPageSettings caps the grid at 20 rows per
        // page — on this shared, long-lived E2E database that's easy to exceed, so a vacancy this
        // test just created (e.g. sorted onto page 2+) can silently sit outside the current page
        // with no indication why. Search first so the grid narrows to just this vacancy regardless
        // of how many others exist (same reasoning as EmployeeListPage.HasEmployeeAsync).
        await SearchAsync(titleFragment);

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
    /// 0=Title, 1=Position Profile, 2=Location, 3=Applications, 4=Status, 5=Opened, 6=Closed.
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

    /// <summary>Reads the "Applications" column's text (VacancyListItemModel.ApplicationCount) for the row matching <paramref name="titleFragment"/>.</summary>
    public Task<string> GetApplicationsColumnTextAsync(string titleFragment) =>
        GetRowCellAsync(titleFragment, columnIndex: 3);

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
}
