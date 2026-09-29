using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeDirectoryReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/employee-directory");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });

        await page.WaitForTimeoutAsync(300);
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var headers = await page.Locator(".e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public Task<bool> HasColumnHeaderAsync(string headerText) =>
        page.Locator(".e-headercell").Filter(new() { HasText = headerText }).First.IsVisibleAsync();

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    public async Task<string?> GetTotalCountTextAsync()
    {
        var summary = page.Locator(".d-flex.justify-content-between.align-items-center div")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("employee\\(s\\)") })
            .First;
        return (await summary.TextContentAsync())?.Trim();
    }


    private ILocator FilterField(string labelText) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = labelText }).First;

    public async Task SelectFilterAsync(string labelText, string valueText)
    {
        await DropDownSelector.SelectAsync(page, FilterField(labelText), valueText);
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }


    private ILocator HeaderCell(string headerText) =>
        page.Locator(".e-headercell").Filter(new() { HasText = headerText }).First;

    public async Task SortByColumnAsync(string headerText)
    {
        var before = await HeaderCell(headerText).GetAttributeAsync("aria-sort");
        await HeaderCell(headerText).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var current = await HeaderCell(headerText).GetAttributeAsync("aria-sort");
            if (current != before) return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    public async Task ResetSortAsync(string headerText)
    {
        for (var i = 0; i < 3 && await GetSortDirectionAsync(headerText) is not null; i++)
        {
            await SortByColumnAsync(headerText);
        }
    }

    public async Task<string?> GetSortDirectionAsync(string headerText) =>
        await HeaderCell(headerText).GetAttributeAsync("aria-sort") switch
        {
            "ascending" => "ascending",
            "descending" => "descending",
            _ => null,
        };


    private ILocator NextPageButton => page.Locator(".e-pagercontainer .e-nextpage");
    private ILocator PreviousPageButton => page.Locator(".e-pagercontainer .e-prevpage");
    private ILocator CurrentPageItem => page.Locator(".e-pagercontainer .e-numericitem.e-currentitem");

    public async Task<bool> IsPagerVisibleAsync() =>
        await page.Locator(".e-pagercontainer").IsVisibleAsync();

    public async Task<bool> IsNextPageDisabledAsync() =>
        await NextPageButton.GetAttributeAsync("aria-disabled") == "true";

    public async Task<bool> IsPreviousPageDisabledAsync() =>
        await PreviousPageButton.GetAttributeAsync("aria-disabled") == "true";

    public async Task ClickNextPageAsync()
    {
        await NextPageButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickPreviousPageAsync()
    {
        await PreviousPageButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<int> GetCurrentPageNumberAsync()
    {
        var text = (await CurrentPageItem.TextContentAsync())?.Trim() ?? "1";
        return int.Parse(text);
    }


    public async Task<IDownload> ExportAsync(string formatLabel)
    {
        return await ReportExport.ExportAsync(page, formatLabel);
    }

    public async Task<string?> GetExportErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();


    private ILocator SavedViewsField =>
        page.Locator(".card-body .col-md-4").Filter(new() { HasText = "Saved Views" }).First;

    public async Task SelectSavedViewAsync(string viewNameOrDisplayText)
    {
        await DropDownSelector.SelectAsync(page, SavedViewsField, viewNameOrDisplayText);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<IReadOnlyList<string>> GetSavedViewOptionTextsAsync()
    {
        var combobox = SavedViewsField.Locator("span[role='combobox']").First;
        await combobox.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var items = await page.Locator(".e-popup.e-ddl .e-list-item").AllAsync();
        var result = new List<string>();
        foreach (var item in items)
            result.Add((await item.TextContentAsync())?.Trim() ?? "");

        await page.Keyboard.PressAsync("Escape");
        return result;
    }

    public ILocator SaveViewDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Save current filters as view" });

    public async Task OpenSaveViewDialogAsync()
    {
        await page.Locator(".report-filter-toolbar button:has(span.e-save)").ClickAsync();
        await SaveViewDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SaveCurrentFiltersAsNewViewAsync(string name)
    {
        await OpenSaveViewDialogAsync();
        await page.GetByPlaceholder("View name").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await SaveViewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task RenameSelectedViewAsync(string newName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename", Exact = true }).ClickAsync();
        await page.GetByPlaceholder("View name").FillAsync(newName);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Name" }).ClickAsync();

        await page.GetByPlaceholder("View name").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task SetSelectedViewAsDefaultAsync()
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Set Default" });
        await button.ClickAsync();
        await Assertions.Expect(button).ToBeDisabledAsync(new() { Timeout = 10_000 });
    }

    public async Task DeleteSelectedViewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetSavedViewErrorAsync()
    {
        var banner = page.Locator(".card-body .alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }
}
