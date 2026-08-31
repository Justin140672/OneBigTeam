using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the three shared ADM-08 governance audit reports, all rendered via
/// src/HR.Web/Components/Pages/Reporting/GovernanceAuditReport.razor:
///   - /companies/{companyId}/reporting/governance/user-activity
///   - /companies/{companyId}/reporting/governance/administrative-changes
///   - /companies/{companyId}/reporting/governance/security-events
///
/// Parameterised by the trailing route segment. The page gates on
/// <c>AppSession.CanViewGovernanceReporting</c> (HR Administrator only); other personas are
/// client-side redirected to /access-denied by AppSession.GuardAccess.
///
/// Layout: an h1 title with a favourite star button (<c>.governance-favourite</c>, active class
/// <c>.governance-favourite--active</c>), a filters card containing a "Saved views" SfDropDownList
/// plus inline save/rename controls, six filter fields (Event type / Status / Actor user ID /
/// Employee ID / From date / To date), "Apply filters" and "Clear" buttons, and a server-paged
/// HrGrid whose toolbar hosts the shared ExportMenu SfDropDownButton (Excel / CSV / PDF).
/// </summary>
public sealed class GovernanceAuditReportPage(IPage page, string baseUrl, string routeSegment)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public static GovernanceAuditReportPage UserActivity(IPage page, string baseUrl) => new(page, baseUrl, "user-activity");
    public static GovernanceAuditReportPage AdministrativeChanges(IPage page, string baseUrl) => new(page, baseUrl, "administrative-changes");
    public static GovernanceAuditReportPage SecurityEvents(IPage page, string baseUrl) => new(page, baseUrl, "security-events");

    public string RelativeUrl => $"/companies/{{0}}/reporting/governance/{routeSegment}";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/governance/{routeSegment}");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        // Give Syncfusion's grid/toolbar JS interop a moment to finish binding — same settle wait
        // the sibling report page objects use after the grid's DOM first appears.
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<bool> IsGridVisibleAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    public async Task<bool> AreFiltersVisibleAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).IsVisibleAsync();

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var headers = await page.Locator(".e-grid .e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    // ── Filters ────────────────────────────────────────────────────────────────

    private ILocator FilterField(string labelText) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = labelText }).First;

    /// <summary>Selects a value in the "Status" SfDropDownList via the shared DropDownSelector — never hand-rolled.</summary>
    public async Task SelectStatusAsync(string statusLabel) =>
        await DropDownSelector.SelectAsync(page, FilterField("Status"), statusLabel);

    public async Task FillEventTypeAsync(string value)
    {
        await FilterField("Event type").Locator("input").First.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillActorUserIdAsync(string value)
    {
        await FilterField("Actor user ID").Locator("input").First.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEmployeeIdAsync(string value)
    {
        await FilterField("Employee ID").Locator("input").First.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<string?> GetFilterErrorAsync()
    {
        var banner = page.Locator(".alert-warning");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    // ── Favourite star ─────────────────────────────────────────────────────────

    private ILocator FavouriteButton => page.Locator(".governance-favourite");

    public async Task<bool> IsFavouritedAsync()
    {
        var cssClass = await FavouriteButton.GetAttributeAsync("class");
        return cssClass?.Contains("governance-favourite--active") == true;
    }

    /// <summary>
    /// Toggles the favourite star, then polls until the CSS class has actually flipped — the toggle
    /// round-trips through ReportingService.Add/RemoveReportFavouriteAsync, so the click dispatching
    /// alone is not proof it committed (same reasoning as ReportCatalogPage.ClickFavouriteAsync).
    /// </summary>
    public async Task ClickFavouriteAsync()
    {
        var wasActive = await IsFavouritedAsync();
        await FavouriteButton.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            if (await IsFavouritedAsync() != wasActive) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for the governance favourite toggle to commit.");
            await page.WaitForTimeoutAsync(150);
        }
    }

    public async Task ReloadAsync()
    {
        await page.ReloadAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await FavouriteButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // ── Export (grid toolbar ExportMenu SfDropDownButton) ───────────────────────

    /// <summary>
    /// Opens the toolbar Export SfDropDownButton and clicks the item matching
    /// <paramref name="formatLabel"/> ("Excel" / "CSV" / "PDF"), returning the triggered download.
    /// </summary>
    public async Task<IDownload> ExportAsync(string formatLabel)
    {
        var downloadTask = page.WaitForDownloadAsync(new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Export" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = formatLabel }).ClickAsync();
        return await downloadTask;
    }

    public async Task<string?> GetExportErrorAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    // ── Saved views (inline controls, not a modal dialog) ───────────────────────

    private ILocator SavedViewsField =>
        page.Locator(".card-body .col-md-4").Filter(new() { HasText = "Saved views" }).First;

    /// <summary>Selects a saved view by name via the shared DropDownSelector; re-applies its filters and reloads the grid.</summary>
    public async Task SelectSavedViewAsync(string viewName)
    {
        await DropDownSelector.SelectAsync(page, SavedViewsField, viewName);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Opens the "Saved views" popup, returns the visible option labels, then closes it again.</summary>
    public async Task<IReadOnlyList<string>> GetSavedViewOptionTextsAsync()
    {
        var combobox = SavedViewsField.Locator("span[role='combobox']").First;
        await combobox.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item").AllAsync();
        var result = new List<string>();
        foreach (var item in items)
            result.Add((await item.TextContentAsync())?.Trim() ?? "");

        await page.Keyboard.PressAsync("Escape");
        return result;
    }

    /// <summary>
    /// Clicks "Save current filters as view", fills the inline "View name" textbox and saves.
    /// The Save SfButton is Disabled while the bound name is whitespace and the HrTextBox only
    /// round-trips its value on blur, so an explicit Tab is required before the click.
    /// </summary>
    public async Task SaveCurrentFiltersAsNewViewAsync(string name)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save current filters as view" }).ClickAsync();
        await page.GetByPlaceholder("View name").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    public async Task<string?> GetSavedViewErrorAsync()
    {
        var banner = page.Locator(".card-body .alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }
}
