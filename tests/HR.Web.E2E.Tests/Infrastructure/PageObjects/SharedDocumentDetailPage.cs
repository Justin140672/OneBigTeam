using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the shared company document detail page
/// (/companies/{companyId}/shared-documents/{documentId}), scoped to the Version History grid,
/// the Publish flow, the Archive flow, the Acknowledgement card's "Edit" flow (used by
/// CompanyDocumentsTabTests to set up documents with acknowledgement requirements before
/// publishing), the Audience card's summary/edit-dialog affordances, the Document Metadata
/// card's "Edit" flow (currently scoped to Review Frequency only — see
/// SharedDocumentReviewFrequencyTests), and navigation to the acknowledgement-progress screen.
/// </summary>
public sealed class SharedDocumentDetailPage(IPage page, string baseUrl)
{
    // The header's Publish and Archive buttons carry an icon that the same-named dialog footer
    // button does not, so filtering by icon (rather than GetByRole(Button, Name: "Publish"/
    // "Archive")) avoids a strict-mode "resolved to N elements" failure once the corresponding
    // dialog is open and both the header button and the dialog's footer button are on screen.
    private ILocator PublishHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-paper-plane") });
    private ILocator ArchiveHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-box-archive") });

    // Unlike Publish/Archive, the page header's "Edit" button shares its icon (fa-pen) and text
    // with the Audience and Acknowledgement cards' own "Edit" buttons, so icon/text filtering
    // can't disambiguate it — it's the only one of the three that isn't inside an .overview-card,
    // and it comes first in DOM order (the header renders before the cards row), so .First is the
    // reliable way to target it specifically.
    private ILocator EditMetadataHeaderButton => page.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First;
    private ILocator EditMetadataDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Metadata" });

    private ILocator PublishDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Document" });
    private ILocator ArchiveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Archive Document" });

    // Scoped to the "Acknowledgement" overview-card so its "Edit" button doesn't collide with the
    // page header's metadata "Edit" button or the "Audience" card's own "Edit" button.
    private ILocator AcknowledgementCard => page.Locator(".overview-card").Filter(new() { HasText = "Acknowledgement" }).First;
    private ILocator EditAcknowledgementDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Acknowledgement Settings" });

    // Scoped to the "Audience" overview-card so its "Edit" button doesn't collide with the page
    // header's metadata "Edit" button or the "Acknowledgement" card's own "Edit" button.
    private ILocator AudienceCard => page.Locator(".overview-card").Filter(new() { HasText = "Audience" }).First;

    public async Task GoToAsync(Guid companyId, Guid documentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}");
        await page.WaitForSelectorAsync(".e-grid, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Navigates to the acknowledgement-progress screen
    /// (SharedDocumentAcknowledgementProgress.razor), reached from this page via the
    /// "Acknowledgement" overview-card's "View Progress" link (only rendered when the document
    /// requires acknowledgement).
    /// </summary>
    public async Task GoToAcknowledgementProgressAsync(Guid companyId, Guid documentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}/acknowledgement-progress");
        await page.WaitForSelectorAsync(".overview-card, .alert-danger", new() { Timeout = 20_000 });
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
    /// Text of the "Review Frequency" row in the Document Metadata card (e.g. "Quarterly" or
    /// "Custom (every 6 months)"), or null when the row isn't rendered at all — SharedDocumentDetail.razor
    /// only renders this row when the frequency isn't "None", so null is the expected result for
    /// a document that has never had a frequency set.
    /// </summary>
    public async Task<string?> GetReviewFrequencyTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Frequency') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to set the Review Frequency (and, when selecting "Custom", the custom months value), then
    /// waits for the page to reload its detail data. <paramref name="frequencyLabel"/> is the
    /// dropdown's display label ("None", "Monthly", "Quarterly", "Six Monthly", "Yearly", "Custom"),
    /// not the underlying enum member name (e.g. "Six Monthly" not "SixMonthly"). Whenever
    /// <paramref name="frequencyLabel"/> isn't "None", a Next Review Date is also filled in — the
    /// dialog requires one whenever the frequency isn't "None", otherwise Save is a no-op client-side.
    /// </summary>
    public async Task SetReviewFrequencyAsync(string frequencyLabel, int? customMonths = null)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Category is the first combobox in this dialog, Review Frequency the second — same
        // click-open/wait-for-popup/click-item interaction pattern used for Category elsewhere
        // in this test suite (see SharedDocumentUploadTests).
        await EditMetadataDialog.Locator("span[role='combobox']").Nth(1).ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = frequencyLabel })
            .First
            .ClickAsync();

        if (frequencyLabel != "None")
        {
            var reviewDateInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("dd/MM/yyyy"));
            await page.Keyboard.PressAsync("Tab");
        }

        if (customMonths.HasValue)
        {
            var monthsInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Custom Frequency" })
                .Locator("input");
            await monthsInput.FillAsync(customMonths.Value.ToString());
        }

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

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

    /// <summary>
    /// Drives the "Acknowledgement" card's "Edit" button and
    /// EditSharedCompanyDocumentAcknowledgementDialog.razor to turn on "Requires employee
    /// acknowledgement" with the given due date, then waits for the page to reload its detail
    /// data. A due date is required — PublishSharedCompanyDocumentHandler rejects publishing a
    /// document that requires acknowledgement but has no due date set, so callers needing
    /// RequiresAcknowledgement=true must call this before <see cref="PublishAsync"/>.
    /// </summary>
    public async Task RequireAcknowledgementAsync(DateOnly dueDate)
    {
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var checkboxWrapper = EditAcknowledgementDialog.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Requires employee acknowledgement" });
        await checkboxWrapper.Locator("label").ClickAsync();

        var dateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dateInput.ClickAsync();
        await dateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");

        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the Publish confirmation dialog via the header "Publish" button, without confirming or cancelling it.</summary>
    public async Task OpenPublishDialogAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsPublishDialogOpenAsync() => PublishDialog.IsVisibleAsync();

    public Task ClickPublishCancelAsync() =>
        PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

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

    /// <summary>Opens the Audience-edit dialog (EditSharedCompanyDocumentAudienceDialog.razor) via the "Audience" overview-card's "Edit" button.</summary>
    public async Task OpenEditAudienceDialogAsync()
    {
        await AudienceCard.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>Text of the "Audience" overview-card's summary line (e.g. "All Employees" or "Departments: Engineering").</summary>
    public async Task<string> GetAudienceSummaryAsync() =>
        (await page.Locator("dt:has-text('Audience') + dd").InnerTextAsync()).Trim();

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
