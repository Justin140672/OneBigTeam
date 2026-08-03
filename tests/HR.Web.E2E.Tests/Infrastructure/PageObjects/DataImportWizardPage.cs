using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee data import wizard: upload -> column mapping -> validate ->
/// preview -> confirm.
/// </summary>
public sealed class DataImportWizardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/data-import/employees");
        await page.WaitForSelectorAsync("input[type='file']", new() { Timeout = 20_000 });
    }

    public async Task UploadFileAsync(string filePath)
    {
        var fileInput = page.Locator("input[type='file']");
        await fileInput.SetInputFilesAsync(filePath);

        await page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        // The Column Mapping step's "Continue" button only renders once column detection has
        // finished (_columns is not null) — the upload response sets the session, which kicks
        // off an async GetSessionColumnsAsync load shown behind a "Detecting columns…" spinner.
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// Reads the selected file-column value from the Column Mapping table for a given standard
    /// field's row (e.g. "First Name"). Scoped to the "2. Column Mapping" card specifically,
    /// since the Upload step's collapsible "Column reference" table also has rows containing
    /// the same standard field names (but no dropdown). Each row's File Column cell is a
    /// Syncfusion SfDropDownList (CssClass "mapping-select"), not a native &lt;select&gt; — its
    /// current value lives in its own combobox's &lt;input&gt;, same convention as every other
    /// SfDropDownList in this suite (see DropDownSelector).
    /// </summary>
    public async Task<string> GetMappingSelectionAsync(string standardHeaderName)
    {
        var mappingCard = page.Locator(".card", new() { HasText = "2. Column Mapping" });

        // Scoped to the Standard Field cell specifically, using an exact text match — every
        // row's dropdown lists ALL detected file headers as options (only one of which is
        // actually selected), so filtering the whole <tr> by HasText is ambiguous: a header
        // name like "First Name" also shows up as an unselected option inside every other
        // row, and a plain HasText filter on <tr> would just match the first row in the table.
        var row = mappingCard.Locator("tr")
            .Filter(new() { Has = page.Locator($"td:text-is(\"{standardHeaderName}\")") })
            .First;
        // Not scoped via ".mapping-select" as an ancestor selector — Syncfusion applies that
        // CssClass directly onto the combobox span itself (role="combobox"), not a wrapping
        // element, so a ".mapping-select span[role='combobox']" descendant selector never matches
        // anything. Same role-only scoping convention as DropDownSelector.
        var combobox = row.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").First.InputValueAsync()).Trim();
    }

    public async Task ContinueFromMappingAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        // "View Preview" button only appears once the validate response has landed
        // (_validation is not null).
        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ViewPreviewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "View Preview" }).ClickAsync();

        // The Preview card header only renders once preview data has loaded. Scoped to the
        // heading role since the stepper nav-link above the card has the identical text.
        await page.GetByRole(AriaRole.Heading, new() { Name = "4. Preview & Confirm" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<bool> HasValidRowAsync(string workEmailFragment)
    {
        // The "4. Preview & Confirm" heading (waited for in ViewPreviewAsync) only proves the
        // Blazor component has mounted, not that Syncfusion's EJ2 grid has finished its own JS
        // render pass to populate ".e-row"/".e-rowcell" — wait for the row selector itself
        // (or its empty-state sibling) before querying it.
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = workEmailFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task ConfirmImportAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Import" }).ClickAsync();

        // The Completion Summary card header only renders once the confirm response has landed.
        await page.GetByText("5. Completion Summary").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string> GetResultStatusAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "5. Completion Summary" });
        var statusDd = resultCard.Locator("dd").First;
        return await statusDd.InnerTextAsync();
    }

    public async Task<int> GetCreatedCountAsync()
    {
        var resultCard = page.Locator(".card", new() { HasText = "5. Completion Summary" });
        var createdDd = resultCard.Locator("dd").Nth(1);
        return int.Parse(await createdDd.InnerTextAsync());
    }

    /// <summary>
    /// Clicks "Download Template" on the Upload step (only visible before a file is uploaded)
    /// and returns the suggested filename of the resulting browser download.
    /// </summary>
    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Template" }).ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    /// <summary>
    /// Clicks "Download Error Report" (rendered on the Preview &amp; Confirm step once row
    /// errors are present, or on the Completion Summary step once failed rows are present) and
    /// returns the suggested filename of the resulting browser download.
    /// </summary>
    public async Task<string> ClickDownloadErrorReportAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Error Report" }).First.ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }
}
