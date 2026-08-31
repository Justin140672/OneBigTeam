using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the standalone ADM-08 Compliance Status governance report
/// (/companies/{companyId}/reporting/governance/compliance-status —
/// GovernanceComplianceStatusReportPage.razor).
///
/// Gates on <c>AppSession.CanViewGovernanceReporting</c> (HR Administrator only); other personas
/// are client-side redirected to /access-denied.
///
/// Layout mirrors the shared governance audit report (favourite star <c>.governance-favourite</c>,
/// "Saved views" controls, server-paged HrGrid with the shared ExportMenu toolbar), but with its
/// own filter set: Category / Severity / Department SfDropDownLists, a filterable Manager picker
/// (like ComplianceCentrePage), and a "Due date from" / "Due date to" range.
/// </summary>
public sealed class GovernanceComplianceStatusReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/governance/compliance-status");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<bool> IsGridVisibleAsync() => await page.Locator(".e-grid").IsVisibleAsync();

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

    public async Task SelectCategoryAsync(string label) =>
        await DropDownSelector.SelectAsync(page, FilterField("Category"), label);

    public async Task SelectSeverityAsync(string label) =>
        await DropDownSelector.SelectAsync(page, FilterField("Severity"), label);

    public async Task SelectDepartmentAsync(string label) =>
        await DropDownSelector.SelectAsync(page, FilterField("Department"), label);

    /// <summary>Selects a manager in the filterable Manager picker via the shared DropDownSelector (like ComplianceCentrePage).</summary>
    public async Task SelectManagerAsync(string nameFragment) =>
        await DropDownSelector.SelectAsync(page, FilterField("Manager"), nameFragment);

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

    // ── Favourite star ─────────────────────────────────────────────────────────

    private ILocator FavouriteButton => page.Locator(".governance-favourite");

    public async Task<bool> IsFavouritedAsync()
    {
        var cssClass = await FavouriteButton.GetAttributeAsync("class");
        return cssClass?.Contains("governance-favourite--active") == true;
    }

    public async Task ClickFavouriteAsync()
    {
        var wasActive = await IsFavouritedAsync();
        await FavouriteButton.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            if (await IsFavouritedAsync() != wasActive) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for the compliance-status favourite toggle to commit.");
            await page.WaitForTimeoutAsync(150);
        }
    }

    public async Task ReloadAsync()
    {
        await page.ReloadAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await FavouriteButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // ── Export ─────────────────────────────────────────────────────────────────

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

    // ── Saved views ────────────────────────────────────────────────────────────

    private ILocator SavedViewsField =>
        page.Locator(".card-body .col-md-4").Filter(new() { HasText = "Saved views" }).First;

    public async Task SelectSavedViewAsync(string viewName)
    {
        await DropDownSelector.SelectAsync(page, SavedViewsField, viewName);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

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
