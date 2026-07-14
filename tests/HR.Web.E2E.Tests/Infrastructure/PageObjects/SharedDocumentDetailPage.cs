using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the shared company document detail page
/// (/companies/{companyId}/shared-documents/{documentId}), scoped to the Version History grid,
/// the Publish flow, and the Archive flow. Its "Publication Status" / "Effective Date" /
/// "Download" columns, and the "Upload New Version" flow
/// (UploadSharedCompanyDocumentVersionDialog.razor) that adds rows to it, are covered here too.
/// Metadata and audience/acknowledgement-edit affordances on this page have their own coverage
/// elsewhere and are intentionally not exposed here.
/// </summary>
public sealed class SharedDocumentDetailPage(IPage page, string baseUrl)
{
    // The header's Publish and Archive buttons carry an icon that the same-named dialog footer
    // button does not, so filtering by icon (rather than GetByRole(Button, Name: "Publish"/
    // "Archive")) avoids a strict-mode "resolved to N elements" failure once the corresponding
    // dialog is open and both the header button and the dialog's footer button are on screen.
    private ILocator PublishHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-paper-plane") });
    private ILocator ArchiveHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-box-archive") });

    private ILocator PublishDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Document" });
    private ILocator ArchiveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Archive Document" });

    public async Task GoToAsync(Guid companyId, Guid documentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}");
        await page.WaitForSelectorAsync(".e-grid, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Text of the Status badge in the Document Metadata card (e.g. "Draft", "Published", "Archived").</summary>
    public async Task<string> GetStatusAsync() =>
        (await page.Locator("dt:has-text('Status') + dd .badge").InnerTextAsync()).Trim();

    public Task<bool> IsArchiveButtonVisibleAsync() => ArchiveHeaderButton.IsVisibleAsync();

    public Task<bool> IsPublishButtonVisibleAsync() => PublishHeaderButton.IsVisibleAsync();

    /// <summary>
    /// Drives the header "Publish" button and its confirmation dialog to completion, then waits
    /// for the page to reload its detail data.
    /// </summary>
    public async Task PublishAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the Archive confirmation dialog via the header "Archive" button.</summary>
    public async Task OpenArchiveDialogAsync()
    {
        await ArchiveHeaderButton.ClickAsync();
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsArchiveDialogOpenAsync() => ArchiveDialog.IsVisibleAsync();

    public Task FillArchiveReasonAsync(string reason) => page.Locator("#archive-reason").FillAsync(reason);

    /// <summary>Clicks the dialog's own "Archive"/"Archiving…" footer button (does not wait for the dialog to close, since an empty reason keeps it open).</summary>
    public Task ClickArchiveConfirmAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();

    public Task ClickArchiveCancelAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    /// <summary>The inline validation/error text shown inside the Archive dialog (e.g. "A reason is required."), or null if none is shown.</summary>
    public async Task<string?> GetArchiveErrorAsync()
    {
        var error = ArchiveDialog.Locator(".alert-danger");
        if (!await error.IsVisibleAsync()) return null;
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Fills in the reason and confirms, then waits for the dialog to close and the page to
    /// reload its detail data. Assumes the reason is non-blank (a blank reason keeps the dialog
    /// open — use OpenArchiveDialogAsync/ClickArchiveConfirmAsync/GetArchiveErrorAsync directly
    /// to exercise that validation path).
    /// </summary>
    public async Task ArchiveAsync(string reason)
    {
        await OpenArchiveDialogAsync();
        await FillArchiveReasonAsync(reason);
        await ClickArchiveConfirmAsync();
        await WaitForArchiveDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful archive to close the dialog and the page to finish reloading its
    /// detail data. Only resolves once the reason was accepted — a validation failure leaves the
    /// dialog open, so callers exercising that path should assert on GetArchiveErrorAsync/
    /// IsArchiveDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForArchiveDialogToCloseAsync()
    {
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>The "Created by / Last updated by / Published by / Archived by" summary line at the bottom of the page.</summary>
    public async Task<string> GetFooterSummaryTextAsync() =>
        (await page.Locator("p.text-muted.small.mb-0").InnerTextAsync()).Trim();

    public async Task<int> GetVersionRowCountAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync();

    /// <summary>Header text of every column currently rendered on the Version History grid.</summary>
    public async Task<IReadOnlyList<string>> GetVersionColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-grid .e-headercell").AllInnerTextsAsync();
        return headers.Select(h => h.Trim()).ToList();
    }

    /// <summary>
    /// Returns the text of the given 0-based column index for the Version History grid row whose
    /// text contains <paramref name="rowTextFragment"/> (e.g. the version's uploaded file name,
    /// which is unique per version in these tests). Column order matches
    /// SharedDocumentDetail.razor's Version History GridColumns: 0=Version,
    /// 1=Publication Status, 2=File Name, 3=Note, 4=Required Ack, 5=Effective Date,
    /// 6=Uploaded By, 7=Uploaded At, 8=Download.
    /// </summary>
    public async Task<string> GetVersionRowCellAsync(string rowTextFragment, int columnIndex)
    {
        var row = page.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// The href of the per-version Download link (fa-download icon, title="Download this
    /// version") in the row whose text contains <paramref name="rowTextFragment"/>. Points at
    /// api/companies/{companyId}/shared-documents/{documentId}/versions/{versionNumber}/download
    /// — a relative &lt;a&gt; the browser follows directly, so no live download needs to occur
    /// for this to be asserted against.
    /// </summary>
    public async Task<string?> GetVersionDownloadHrefAsync(string rowTextFragment)
    {
        var row = page.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return await row.Locator("a[title='Download this version']").GetAttributeAsync("href");
    }

    /// <summary>
    /// Drives the "Upload New Version" button and its dialog
    /// (UploadSharedCompanyDocumentVersionDialog.razor) to completion, then waits for the page's
    /// Version History grid to refresh with the new version.
    /// </summary>
    public async Task UploadNewVersionAsync(string versionNote, string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload New Version" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload New Version" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("What changed in this version?").FillAsync(versionNote);
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
