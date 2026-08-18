using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the shared company document detail page
/// (/companies/{companyId}/shared-documents/{documentId}), scoped to the Version History grid,
/// the Publish flow, the Archive flow, the "Mark Expired" flow (see SharedDocumentExpireTests),
/// the Complete Review flow (CompleteSharedCompanyDocumentReviewDialog.razor —
/// see SharedDocumentCompleteReviewTests), the Acknowledgement card's "Edit" flow (used by
/// CompanyDocumentsTabTests to set up documents with acknowledgement requirements before
/// publishing), the Audience card's summary/edit-dialog affordances, the Document Metadata
/// card's "Edit" flow (covering Review Frequency — see SharedDocumentReviewFrequencyTests — and
/// Review Owner — see SharedDocumentReviewOwnerTests), and navigation to the
/// acknowledgement-progress screen.
/// </summary>
public sealed class SharedDocumentDetailPage(IPage page, string baseUrl)
{
    // The header's Publish and Archive buttons carry an icon that the same-named dialog footer
    // button does not, so filtering by icon (rather than GetByRole(Button, Name: "Publish"/
    // "Archive")) avoids a strict-mode "resolved to N elements" failure once the corresponding
    // dialog is open and both the header button and the dialog's footer button are on screen.
    private ILocator PublishHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-paper-plane") });
    private ILocator ArchiveHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-box-archive") });

    // Same icon-filter disambiguation reasoning as ArchiveHeaderButton above — fa-calendar-xmark
    // is unique to this header button (it isn't reused by the dialog's own footer button, which
    // carries no icon).
    private ILocator ExpireHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-calendar-xmark") });

    // The "Review Document" header button also carries the fa-clipboard-check icon that the
    // Acknowledgement overview-card's header uses, but that header icon lives on a plain <span>
    // (not a button), so filtering GetByRole(Button) by this icon still resolves to just the one
    // header button — same reasoning as Publish/Archive above.
    private ILocator ReviewHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-clipboard-check") });

    // Unlike Publish/Archive, the page header's "Edit" button shares its icon (fa-pen) and text
    // with the Audience and Acknowledgement cards' own "Edit" buttons, so icon/text filtering
    // can't disambiguate it — it's the only one of the three that isn't inside an .overview-card,
    // and it comes first in DOM order (the header renders before the cards row), so .First is the
    // reliable way to target it specifically.
    private ILocator EditMetadataHeaderButton => page.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First;
    private ILocator EditMetadataDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Metadata" });

    private ILocator PublishDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Document" });
    private ILocator ArchiveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Archive Document" });
    private ILocator ExpireDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Mark Document as Expired" });
    private ILocator ReviewDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Complete Review" });

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

    /// <summary>Page header's document title (the &lt;h1&gt; — SharedDocumentDetail.razor renders @_detail.Title there).</summary>
    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").First.InnerTextAsync()).Trim();

    /// <summary>Text of the "Category" row in the Document Metadata card.</summary>
    public async Task<string> GetCategoryAsync() =>
        (await page.Locator("dt:has-text('Category') + dd").InnerTextAsync()).Trim();

    /// <summary>
    /// Text of the "Description" row in the Document Metadata card, or null when the row isn't
    /// rendered at all — SharedDocumentDetail.razor only renders this row once Description is
    /// non-blank.
    /// </summary>
    public async Task<string?> GetDescriptionAsync()
    {
        var row = page.Locator("dt:has-text('Description') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor to
    /// change Title, Description, and Category together, then waits for the page to reload its
    /// detail data. <paramref name="categoryLabel"/> is matched against the Category dropdown's
    /// list items (e.g. "Handbook") — same click-open/wait-for-popup/click-item pattern as
    /// SetReviewFrequencyAsync above.
    /// </summary>
    // See the remarks on the FillAsync -> ClearAndTypeAsync switch in EditTitleDescriptionCategoryAsync.
    private async Task ClearAndTypeAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task EditTitleDescriptionCategoryAsync(string title, string description, string categoryLabel)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Plain FillAsync sets the native input's value and fires a single "input" event, which
        // doesn't reliably replace a Syncfusion SfTextBox's own tracked value — observed as the
        // saved title being the *old* title with the new one appended (e.g. "Test Policy
        // <guid>Updated Handbook <guid>") rather than replaced. Select-all + delete first, same
        // mitigation already applied to CompanyEditPage.TypeIntoTextBoxAsync and
        // EmployeeEditPage.TypeIntoNumericInputAsync for the identical class of race.
        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Document title"), title);
        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Optional description"), description);

        var categoryGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(page, categoryGroup, categoryLabel);

        // Confirms the Blazor round-trip committed Model.CategoryId, not just that the popup
        // closed client-side (same pattern as SetReviewFrequencyAsync).
        await Assertions.Expect(categoryGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(categoryLabel)), new() { Timeout = 10_000 });

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Dialog-hidden isn't proof the page's own detail reload has landed in the DOM yet — the
        // h1 can still show the pre-edit title for a beat after the dialog closes. Assert directly
        // on the h1's content (auto-retrying) rather than a spinner-clear proxy, same pattern used
        // for CompleteReview's footer summary elsewhere in this file.
        await Assertions.Expect(page.Locator("h1").First)
            .ToHaveTextAsync(title, new() { Timeout = 15_000 });
    }

    public Task<bool> IsArchiveButtonVisibleAsync() => ArchiveHeaderButton.IsVisibleAsync();

    public Task<bool> IsExpireButtonVisibleAsync() => ExpireHeaderButton.IsVisibleAsync();

    public Task<bool> IsPublishButtonVisibleAsync() => PublishHeaderButton.IsVisibleAsync();

    public Task<bool> IsReviewButtonVisibleAsync() => ReviewHeaderButton.IsVisibleAsync();

    /// <summary>
    /// Text of the "Next Review Date" row in the Document Metadata card (e.g. "16 August 2026"),
    /// or null when the row isn't rendered at all — SharedDocumentDetail.razor only renders this
    /// row once ReviewDate has a value.
    /// </summary>
    public async Task<string?> GetReviewDateTextAsync()
    {
        var row = page.Locator("dt:has-text('Next Review Date') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

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

        // Scoped to the "Review Frequency" field's own ".col-md-6" group (rather than by combobox
        // index) since its combobox can render before Category's — Category's is gated behind an
        // async data load while Review Frequency's isn't — same click-open/wait-for-popup/
        // click-item interaction pattern used for Category elsewhere in this test suite (see
        // SharedDocumentUploadTests).
        var reviewFrequencyGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
        await DropDownSelector.SelectAsync(page, reviewFrequencyGroup, frequencyLabel);

        // Confirms the Blazor round-trip committed the selection, not just that the popup closed
        // client-side (same pattern as the Review Owner selector below /
        // EmployeeEditPage.SelectManagerAsync). Without this, an immediate Save can race the
        // round-trip.
        await Assertions.Expect(reviewFrequencyGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(frequencyLabel)), new() { Timeout = 10_000 });

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
            // SfNumericTextBox: a bare FillAsync bypasses its interop entirely — retype for real
            // (same convention as EmployeeEditPage.TypeIntoNumericInputAsync).
            var monthsInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Custom Frequency" })
                .Locator("input");
            await monthsInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await monthsInput.PressSequentiallyAsync(customMonths.Value.ToString());
            await page.Keyboard.PressAsync("Tab");
        }

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // The spinner-clear wait below is a best-effort proxy that's a no-op if the page's own
        // detail-card reload never shows a spinner at all (e.g. a fast/instant re-render) — a
        // caller that immediately reads GetReviewFrequencyTextAsync() right after can still race
        // the actual reload landing (observed: "Yearly" not found right after setting it). Assert
        // directly on the frequency row's own content instead, same pattern already used for
        // EditTitleDescriptionCategoryAsync's title check above, which auto-retries until the real
        // reload lands rather than trusting a loading-state heuristic. "None" has no dt/dd row at
        // all (SharedDocumentDetail.razor only renders it for a non-None frequency), so only assert
        // when setting a real frequency.
        if (frequencyLabel != "None")
        {
            await Assertions.Expect(page.Locator("dt:has-text('Review Frequency') + dd"))
                .ToContainTextAsync(frequencyLabel == "Custom" ? "Custom" : frequencyLabel, new() { Timeout = 15_000 });
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Text of the "Review Owner" row in the Document Metadata card (e.g. "Marcus Diallo"), or
    /// null when the row isn't rendered at all — SharedDocumentDetail.razor only renders this row
    /// once ReviewOwnerEmployeeId is set, so null is the expected result for a document that has
    /// never had a review owner assigned.
    /// </summary>
    public async Task<string?> GetReviewOwnerTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Owner') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to set (or change) the Review Owner via its filterable employee picker, then waits for the
    /// page to reload its detail data. <paramref name="employeeNameFragment"/> is typed into the
    /// dropdown's filter input to trigger the dialog's server-side search (same
    /// OnReviewOwnerFilteringAsync pattern as the Employment tab's Manager picker — see
    /// EmployeeEditPage.SelectManagerAsync) and must match the full display name exactly enough
    /// to resolve to a single result and to assert against the combobox's resulting input value.
    /// </summary>
    public async Task SetReviewOwnerAsync(string employeeNameFragment)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, employeeNameFragment);

        // Confirms the Blazor round-trip committed Model.ReviewOwnerEmployeeId, not just that the
        // popup closed client-side (same reasoning as EmployeeEditPage.SelectManagerAsync).
        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(employeeNameFragment, new() { Timeout = 10_000 });

        // Even the displayed input value isn't a fully reliable proof of the server-side commit
        // here: Syncfusion's SfDropDownList JS widget updates its own visible <input> (and clear
        // icon) as part of its own client-side selection handling, which can complete slightly
        // ahead of the separate interop call that carries the selection back to Blazor and
        // actually sets Model.ReviewOwnerEmployeeId server-side. Clicking Save in that narrow
        // window submits with the *previous* (still null, on a first-time assignment) value —
        // this is the same class of "visually filled but not yet round-tripped" issue documented
        // on CompanyEditPage.FillNumericAndVerifyAsync, mitigated there with a short buffer before
        // trusting the field's value; do the same here before Save.
        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to clear the Review Owner by opening its dropdown and selecting the prepended "Not
    /// assigned" sentinel item (Id = Guid.Empty), then waits for the page to reload its detail
    /// data. Replaces the old ShowClearButton ("x" icon) approach, which was removed in favor of
    /// this explicit no-selection list item (see EditSharedCompanyDocumentMetadataDialog.razor's
    /// ReviewOwnerOption list, which prepends a Guid.Empty/"Not assigned" entry).
    /// </summary>
    public async Task ClearReviewOwnerAsync()
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, "Not assigned");

        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync("Not assigned", new() { Timeout = 10_000 });

        // Same "visually cleared but not yet round-tripped" concern as SetReviewOwnerAsync above —
        // give the pending ValueChanged interop call a moment to land before Save reads the model.
        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Dialog-hidden isn't proof the page's own detail reload has landed in the DOM yet — the
        // Review Owner row can still show the pre-clear name for a beat after the dialog closes.
        // SharedDocumentDetail.razor only renders the "Review Owner" dt/dd pair at all when
        // ReviewOwnerEmployeeId has a value, so assert the row is gone (auto-retrying) rather than
        // a spinner-clear proxy — same pattern used for EditTitleDescriptionCategoryAsync's h1
        // assertion above.
        await Assertions.Expect(page.Locator("dt:has-text('Review Owner')"))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
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

        // Checking this box flips EditSharedCompanyDocumentAcknowledgementDialog.razor's
        // "@if (Model.RequiresAcknowledgement)" block from unmounted to mounted — the date picker,
        // statement field, etc. don't exist in the DOM at all until this Blazor re-render lands,
        // and Syncfusion's SfDatePicker then does its own follow-up JS-interop initialization pass
        // on top of that. Clicking immediately can catch the element mid-(re)creation and get
        // "element was detached from the DOM, retrying" — Playwright's own retry loop usually
        // recovers from a single such hand-off, but back-to-back re-renders (Blazor mount, then
        // Syncfusion's own init) can outlast even its default retry window under load. Wait for
        // the date input to exist and settle before interacting.
        var dateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dateInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);

        await dateInput.ClickAsync();
        await dateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");

        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    // The header's "Audit History" button carries the fa-clock-rotate-left icon, which is also
    // used by the "Review History" card's own header — but that header icon lives on a plain
    // <span> (not a button), same disambiguation reasoning as ReviewHeaderButton above, so
    // filtering GetByRole(Button) by this icon still resolves to just the one header button.
    private ILocator AuditHistoryHeaderButton => page.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-clock-rotate-left") });
    private ILocator AuditHistoryDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit History" });
    private ILocator AuditDetailDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit Event Detail" });

    /// <summary>Opens the Audit History dialog (SharedCompanyDocumentAuditHistoryDialog.razor) via the header "Audit History" button.</summary>
    public async Task OpenAuditHistoryDialogAsync()
    {
        await AuditHistoryHeaderButton.ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // The dialog container mounting doesn't prove its grid has populated yet — Syncfusion
        // populates ".e-row"/".e-rowcell" data on a separate JS tick (same "container before
        // content" race fixed elsewhere in this suite, e.g. EmployeeAdminPage's asset grids). A
        // caller that immediately calls GetAuditHistoryRowCountAsync can otherwise read 0 for a
        // document that genuinely has audit entries.
        await AuditHistoryDialog.Locator(
            "[data-testid='document-audit-history-grid'] .e-row, [data-testid='document-audit-history-grid'] .e-emptyrow")
            .First.WaitForAsync(new() { Timeout = 10_000 });
    }

    public Task<bool> IsAuditHistoryDialogOpenAsync() => AuditHistoryDialog.IsVisibleAsync();

    /// <summary>
    /// Number of rows currently rendered in the Audit History dialog's grid
    /// (data-testid="document-audit-history-grid"), scoped to the dialog so it can't collide with
    /// any other ".e-row" markup elsewhere on the page.
    /// </summary>
    public Task<int> GetAuditHistoryRowCountAsync() =>
        AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row").CountAsync();

    /// <summary>
    /// Clicks the "View" (fa-eye) button on the Audit History grid row whose text contains
    /// <paramref name="rowTextFragment"/> (e.g. an Action value like "Published" or "Acknowledgement Settings Updated"),
    /// opening the Audit Event Detail dialog.
    /// </summary>
    public async Task ClickViewAuditHistoryRowAsync(string rowTextFragment)
    {
        var row = AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row")
            .Filter(new() { HasText = rowTextFragment })
            .First;
        await row.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsAuditDetailDialogOpenAsync() => AuditDetailDialog.IsVisibleAsync();

    /// <summary>All text content of the Audit Event Detail dialog (Date/User/Action rows plus the Before/After changes table).</summary>
    public async Task<string> GetAuditDetailDialogTextAsync() =>
        (await AuditDetailDialog.InnerTextAsync()).Trim();

    // Scoped to .e-footer-content rather than GetByRole(Button, Name="Close") — the dialog's own
    // ShowCloseIcon header button also carries an accessible name of "Close", so a role/name match
    // resolves to two elements. Mirrors EmployeeEditPage.CloseAuditDetailDialogAsync's established
    // fix for the identical ambiguity on the employee-side audit detail dialog.
    public async Task CloseAuditDetailDialogAsync()
    {
        await AuditDetailDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CloseAuditHistoryDialogAsync()
    {
        await AuditHistoryDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Edit Acknowledgement Settings dialog (statement field, Reset to Default, publish-lock) ──

    /// <summary>
    /// Opens the "Acknowledgement" card's Edit dialog without changing/saving anything — unlike
    /// <see cref="RequireAcknowledgementAsync"/>, which drives the whole toggle-and-save flow, this
    /// is for tests that need to inspect or interact with the dialog's fields directly (e.g. the
    /// statement field's locked/editable state, or the "Reset to Default" button).
    /// </summary>
    public async Task OpenEditAcknowledgementDialogAsync()
    {
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>
    /// Syncfusion's modal overlay (".e-dlg-overlay") is a DOM sibling of the dialog itself, not a
    /// descendant, and its close-fade animation can still be intercepting pointer events for a
    /// short moment after the dialog role element itself already reports "Hidden" to Playwright.
    /// A caller that immediately re-clicks the same "Edit" button to reopen the dialog (e.g.
    /// RequireAcknowledgementAsync followed by OpenEditAcknowledgementDialogAsync, or
    /// SaveEditAcknowledgementDialogAsync followed by another OpenEditAcknowledgementDialogAsync)
    /// can otherwise hit "subtree intercepts pointer events" on the stale overlay. Best-effort: if
    /// no overlay is present at all, this is a no-op.
    /// </summary>
    private async Task WaitForOverlayToClearAsync()
    {
        try
        {
            await page.Locator(".e-dlg-overlay").WaitForAsync(
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Ignore — best-effort settle only.
        }
    }

    public Task<bool> IsEditAcknowledgementDialogOpenAsync() => EditAcknowledgementDialog.IsVisibleAsync();

    // The Acknowledgement Statement field is the only HrTextBox (Multiline, so a <textarea>)
    // rendered inside this dialog.
    private ILocator AcknowledgementStatementTextArea => EditAcknowledgementDialog.Locator("textarea");

    public Task<string> GetAcknowledgementStatementValueAsync() =>
        AcknowledgementStatementTextArea.InputValueAsync();

    public async Task FillAcknowledgementStatementAsync(string value)
    {
        await AcknowledgementStatementTextArea.FillAsync(value);

        // HrTextBox (SfTextBox under the hood) only round-trips its bound value over the Blazor
        // Server circuit on blur/change — same reasoning as CompanyEditPage.TypeIntoTextBoxAsync
        // elsewhere in this suite. A page-level Tab keypress is the wrong way to trigger that blur
        // here, though: this textarea isn't recognized by SfDialog's own focus trap, so Tab can
        // carry focus straight out of the dialog entirely — which SfDialog treats as an
        // outside-focus/close event, firing its own Closed handler
        // (EditSharedCompanyDocumentAcknowledgementDialog's DialogEvents Closed="HandleDialogClosed")
        // and popping the "Unsaved Changes" confirmation dialog on top instead of just committing
        // the value (surfaced as the Save button becoming permanently unclickable/the dialog never
        // closing afterward). Click the Due Date field instead — a genuinely focusable element that
        // stays inside the dialog's own DOM subtree, so it blurs the textarea without ever handing
        // focus outside the dialog. Only rendered alongside the statement field, gated by the same
        // Model.RequiresAcknowledgement condition, so it's always present when this method is.
        var dueDateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dueDateInput.ClickAsync();

        // That commit also causes Syncfusion to destroy and recreate the textarea's own DOM node
        // (rather than patch it in place), so polling a *freshly re-resolved* locator each time —
        // rather than holding a single ElementHandle across the round-trip — avoids racing that
        // teardown. Same class of fix as DataImportWizardPage.GetMappingSelectionAsync's poll.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await AcknowledgementStatementTextArea.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    /// <summary>
    /// True once the document's Status is no longer "Draft" — EditSharedCompanyDocumentAcknowledgementDialog.razor
    /// sets HrTextBox's Readonly to this, rendering a "readonly" attribute on the underlying textarea.
    /// </summary>
    public async Task<bool> IsAcknowledgementStatementReadOnlyAsync() =>
        await AcknowledgementStatementTextArea.GetAttributeAsync("readonly") is not null;

    private ILocator ResetAcknowledgementStatementButton =>
        EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Reset to Default" });

    public Task<bool> IsResetAcknowledgementStatementButtonDisabledAsync() =>
        ResetAcknowledgementStatementButton.IsDisabledAsync();

    /// <summary>
    /// Clicks "Reset to Default" in the Edit Acknowledgement Settings dialog. Immediately after
    /// the dialog opens, Syncfusion is still settling its own re-render of the dialog body (same
    /// destroy/recreate-on-bind churn documented on FillAcknowledgementStatementAsync above), which
    /// can detach the button out from under a single ClickAsync's actionability wait — surfaced as
    /// "element is not stable"/"was detached from the DOM, retrying" — Playwright's own built-in
    /// retry only covers the current element handle, not the button being torn down and rebuilt.
    /// Re-resolving the locator per attempt and retrying the click itself rides out that churn.
    /// </summary>
    public async Task ClickResetAcknowledgementStatementToDefaultAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            try
            {
                await ResetAcknowledgementStatementButton.ClickAsync(new() { Timeout = 5_000 });
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }
    }

    /// <summary>
    /// Whether the "Locked after publishing — upload a new version to change the wording." note is
    /// shown, which EditSharedCompanyDocumentAcknowledgementDialog.razor only renders once the
    /// statement field is locked (Status != "Draft").
    /// </summary>
    public Task<bool> IsAcknowledgementLockedNoteVisibleAsync() =>
        EditAcknowledgementDialog.GetByText("Locked after publishing").IsVisibleAsync();

    /// <summary>Saves the Edit Acknowledgement Settings dialog, then waits for it to close and the page to reload its detail data.</summary>
    public async Task SaveEditAcknowledgementDialogAsync()
    {
        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

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

    public async Task ClickPublishCancelAsync()
    {
        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Opens the Archive confirmation dialog via the header "Archive" button.</summary>
    public async Task OpenArchiveDialogAsync()
    {
        await ArchiveHeaderButton.ClickAsync();
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsArchiveDialogOpenAsync() => ArchiveDialog.IsVisibleAsync();

    public async Task FillArchiveReasonAsync(string reason)
    {
        await page.Locator("#archive-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks the dialog's own "Archive"/"Archiving…" footer button (does not wait for the dialog to close, since an empty reason keeps it open).</summary>
    public Task ClickArchiveConfirmAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();

    public Task ClickArchiveCancelAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    /// <summary>The inline validation/error text shown inside the Archive dialog (e.g. "A reason is required."), or null if none is shown.</summary>
    public async Task<string?> GetArchiveErrorAsync()
    {
        var error = ArchiveDialog.Locator(".alert-danger");
        // A bare instant IsVisibleAsync() right after ClickArchiveConfirmAsync() can race the
        // client-side validation render (same "read before it settles" class of race fixed
        // elsewhere in this suite) and return null before the message has actually appeared. Give
        // it a bounded wait instead of failing fast.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
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

    /// <summary>Opens the "Mark Document as Expired" confirmation dialog via the header "Mark Expired" button.</summary>
    public async Task OpenExpireDialogAsync()
    {
        await ExpireHeaderButton.ClickAsync();
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsExpireDialogOpenAsync() => ExpireDialog.IsVisibleAsync();

    /// <summary>The Expire dialog's body paragraph text (e.g. confirming the document title and consequences of expiring it).</summary>
    public async Task<string> GetExpireDialogBodyTextAsync() =>
        (await ExpireDialog.Locator("p").First.InnerTextAsync()).Trim();

    /// <summary>Clicks the dialog's own "Mark Expired"/"Marking Expired…" footer button (does not wait for the dialog to close).</summary>
    public Task ClickExpireConfirmAsync() =>
        ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Mark Expired", Exact = true }).ClickAsync();

    public async Task ClickExpireCancelAsync()
    {
        await ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>The inline error text shown inside the Expire dialog when the server rejects the request (e.g. already Expired/Archived), or null if none is shown.</summary>
    public async Task<string?> GetExpireErrorAsync()
    {
        var error = ExpireDialog.Locator(".alert-danger");
        // Same "read before it settles" race as GetArchiveErrorAsync/GetReviewValidationErrorAsync
        // above — a bare instant IsVisibleAsync() right after confirming can race the error
        // banner's render and return null before it has actually appeared.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the header "Mark Expired" button and its confirmation dialog to completion, then
    /// waits for the dialog to close and the page to reload its detail data.
    /// </summary>
    public async Task ExpireAsync()
    {
        await OpenExpireDialogAsync();
        await ClickExpireConfirmAsync();
        await WaitForExpireDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful expire to close the dialog and the page to finish reloading its
    /// detail data. Only resolves once the request succeeded — a server-side rejection leaves the
    /// dialog open, so callers exercising that path should assert on GetExpireErrorAsync/
    /// IsExpireDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForExpireDialogToCloseAsync()
    {
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the Complete Review dialog (CompleteSharedCompanyDocumentReviewDialog.razor) via the header "Review Document" button, without confirming or cancelling it.</summary>
    public async Task OpenReviewDialogAsync()
    {
        await ReviewHeaderButton.ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsReviewDialogOpenAsync() => ReviewDialog.IsVisibleAsync();

    /// <summary>
    /// Text of a labelled dt/dd metadata row inside the Complete Review dialog (e.g. "Title",
    /// "Category", "Next Review Date", "Review Frequency", "Review Owner"), or null when the row
    /// isn't rendered (Review Frequency and Review Owner are only rendered when set — same as the
    /// page's own Document Metadata card). Deliberately scoped to the dialog itself rather than a
    /// bare "dt:has-text(...) + dd" page-level locator, since SharedDocumentDetail.razor's own
    /// Document Metadata card renders dt/dd rows with these exact same labels underneath the
    /// dialog, which would otherwise resolve to two elements.
    /// </summary>
    public async Task<string?> GetReviewDialogMetadataRowAsync(string label)
    {
        var row = ReviewDialog.Locator($"dt:has-text('{label}') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task FillReviewNotesAsync(string notes)
    {
        await page.Locator("#review-notes").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks the dialog's own "Complete Review"/"Saving…" footer button (does not wait for the dialog to close, since blank/whitespace notes keeps it open).</summary>
    public Task ClickReviewConfirmAsync() =>
        ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Complete Review", Exact = true }).ClickAsync();

    public async Task ClickReviewCancelAsync()
    {
        await ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>The inline validation message shown inside the Complete Review dialog when notes are blank/whitespace (e.g. "Review notes are required."), or null if none is shown.</summary>
    public async Task<string?> GetReviewValidationErrorAsync()
    {
        var error = ReviewDialog.Locator(".text-danger.small.mt-1");
        // Same "read before it settles" race as GetArchiveErrorAsync above — a bare instant
        // IsVisibleAsync() right after ClickReviewConfirmAsync() can race the client-side
        // validation render and return null before the message has actually appeared.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Fills in the review notes and confirms, then waits for the dialog to close and the page to
    /// reload its detail data. Assumes the notes are non-blank (blank/whitespace notes keep the
    /// dialog open — use OpenReviewDialogAsync/ClickReviewConfirmAsync/GetReviewValidationErrorAsync
    /// directly to exercise that validation path).
    /// </summary>
    public async Task CompleteReviewAsync(string notes)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful review completion to close the dialog and the page to finish
    /// reloading its detail data. Only resolves once the notes were accepted — a validation
    /// failure leaves the dialog open, so callers exercising that path should assert on
    /// GetReviewValidationErrorAsync/IsReviewDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForReviewDialogToCloseAsync()
    {
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Sets a file on the Complete Review dialog's optional "Renewed File" input
    /// (CompleteSharedCompanyDocumentReviewDialog.razor). Attaching a file here (in addition to
    /// the required review notes) drives the dialog to also call
    /// DocumentService.UploadSharedCompanyDocumentVersionAsync before completing the review,
    /// adding a new row to the Version History grid — see SharedDocumentReviewRenewalTests.
    /// </summary>
    public Task SetReviewRenewedFileAsync(string filePath) =>
        ReviewDialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    // Scoped to the Complete Review dialog so this doesn't collide with the same-labelled
    // checkbox rendered by UploadSharedCompanyDocumentVersionDialog.razor's own "Upload New
    // Version" dialog (both use the identical SfCheckBox label).
    private ILocator ReviewReacknowledgementCheckbox => ReviewDialog.Locator(".e-checkbox-wrapper")
        .Filter(new() { HasText = "Requires employees to acknowledge this version again" });

    /// <summary>
    /// Whether the "Requires employees to acknowledge this version again" checkbox is currently
    /// rendered inside the Complete Review dialog. CompleteSharedCompanyDocumentReviewDialog.razor
    /// only renders it once the document's RequiresAcknowledgement is true AND a "Renewed File"
    /// has been selected via <see cref="SetReviewRenewedFileAsync"/> — see
    /// SharedDocumentReviewRenewalTests.
    /// </summary>
    public Task<bool> IsReviewReacknowledgementCheckboxVisibleAsync() =>
        ReviewReacknowledgementCheckbox.WaitUntilVisibleAsync();

    /// <summary>
    /// Checks the Complete Review dialog's "Requires employees to acknowledge this version
    /// again" checkbox. Assumes it's currently rendered — i.e. a file has already been selected
    /// via <see cref="SetReviewRenewedFileAsync"/> on a document with RequiresAcknowledgement true.
    /// </summary>
    public Task CheckReviewReacknowledgementAsync() => ReviewReacknowledgementCheckbox.Locator("label").ClickAsync();

    /// <summary>
    /// Fills in the review notes, attaches a "Renewed File", optionally checks the
    /// reacknowledgement checkbox, and confirms — driving the same notes-then-upload-then-
    /// complete-review flow CompleteSharedCompanyDocumentReviewDialog.razor's ConfirmAsync
    /// performs once a file is selected — then waits for the dialog to close and the page to
    /// reload its detail data (including the Version History grid). Assumes the notes are
    /// non-blank and the file is a valid upload (see <see cref="CompleteReviewAsync"/> for the
    /// notes-only, validation-focused equivalent).
    /// </summary>
    public async Task CompleteReviewWithRenewedFileAsync(string notes, string filePath, bool requiresReacknowledgement = false)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await SetReviewRenewedFileAsync(filePath);

        if (requiresReacknowledgement)
        {
            await CheckReviewReacknowledgementAsync();
        }

        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
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
    public ILocator FooterSummary => page.Locator("p.text-muted.small.mb-0");

    public async Task<string> GetFooterSummaryTextAsync() =>
        (await FooterSummary.InnerTextAsync()).Trim();

    /// <summary>
    /// The Version History card's own container — same ".overview-card" scoping pattern as
    /// <see cref="ReviewHistoryCard"/>. The page now has TWO grids that both render ".e-row"/
    /// ".e-grid .e-headercell" markup (Version History and, since the "Display Review Information"
    /// story, Review History) — an unscoped page-wide ".e-grid .e-row"/".e-row" locator silently
    /// counts both grids' rows combined, which is exactly the kind of bug that made
    /// WaitForVersionRowCountAsync(2) actually observe 3 (2 versions + 1 review history row) once
    /// a test's flow both uploads a version *and* completes a review. Every version-row locator
    /// below is scoped to this card specifically to avoid that collision.
    /// </summary>
    private ILocator VersionHistoryCard => page.Locator(".overview-card").Filter(new() { HasText = "Version History" }).First;

    public async Task<int> GetVersionRowCountAsync() =>
        await VersionHistoryCard.Locator(".e-row").CountAsync();

    /// <summary>
    /// Waits for the Version History grid to show exactly <paramref name="expectedCount"/> rows,
    /// then returns that count. Unlike <see cref="GetVersionRowCountAsync"/> (a one-shot,
    /// non-retrying <c>CountAsync()</c> snapshot), this polls via Playwright's own
    /// <c>ToHaveCountAsync</c> assertion — necessary because neither
    /// <see cref="UploadNewVersionAsync"/>'s "dialog hidden" nor its "spinner gone" wait guarantees
    /// the Blazor Server render carrying the new row has actually been flushed over SignalR and
    /// painted into the DOM by the time control returns to the caller; an immediate one-shot count
    /// can catch the grid mid-update and read one row short.
    /// </summary>
    public async Task<int> WaitForVersionRowCountAsync(int expectedCount)
    {
        var rows = VersionHistoryCard.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    /// <summary>Header text of every column currently rendered on the Version History grid.</summary>
    public async Task<IReadOnlyList<string>> GetVersionColumnHeadersAsync()
    {
        var headers = await VersionHistoryCard.Locator(".e-grid .e-headercell").AllInnerTextsAsync();
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
        var row = VersionHistoryCard.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
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
        var row = VersionHistoryCard.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
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
        await page.Keyboard.PressAsync("Tab");
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    // Scoped to the "Review History" overview-card so its read-only grid (populated by
    // CompleteSharedCompanyDocumentReviewDialog.razor — see SharedDocumentCompleteReviewTests)
    // isn't confused with the "Version History" card's own grid immediately above it — both use
    // the shared HrGrid component and so share ".e-grid"/".e-row"/".e-headercell" class names, but
    // "Review History" doesn't appear as a substring of "Version History" (or any other card title
    // on this page), so filtering by it alone is safe without needing icon-based disambiguation.
    private ILocator ReviewHistoryCard => page.Locator(".overview-card").Filter(new() { HasText = "Review History" }).First;

    public Task<bool> IsReviewHistoryCardVisibleAsync() => ReviewHistoryCard.IsVisibleAsync();

    /// <summary>
    /// Waits for the Review History grid to finish its own JS render tick (a data row or its
    /// ".e-emptyrow" empty-state sibling present — same "'.e-grid' alone doesn't prove rows are
    /// queryable" reasoning as e.g. CandidateListPage.RowsRenderedSelector), then returns the
    /// number of actual data rows (0 for the empty-state case, since ".e-emptyrow" itself is
    /// excluded from this count).
    /// </summary>
    public async Task<int> GetReviewHistoryRowCountAsync()
    {
        await ReviewHistoryCard.Locator(".e-row, .e-emptyrow").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await ReviewHistoryCard.Locator(".e-row").CountAsync();
    }

    /// <summary>
    /// Waits for the Review History grid to show exactly <paramref name="expectedCount"/> rows,
    /// then returns that count. Unlike <see cref="GetReviewHistoryRowCountAsync"/> (a one-shot
    /// snapshot), this polls via Playwright's own <c>ToHaveCountAsync</c> assertion — necessary
    /// because neither <see cref="CompleteReviewAsync"/>'s "dialog hidden" nor its "spinner gone"
    /// wait guarantees the Blazor Server render carrying the new history row has actually been
    /// flushed over SignalR and painted into the DOM by the time control returns to the caller
    /// (same reasoning as <see cref="WaitForVersionRowCountAsync"/>).
    /// </summary>
    public async Task<int> WaitForReviewHistoryRowCountAsync(int expectedCount)
    {
        var rows = ReviewHistoryCard.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    /// <summary>Header text of every column currently rendered on the Review History grid.</summary>
    public async Task<IReadOnlyList<string>> GetReviewHistoryColumnHeadersAsync()
    {
        var headers = await ReviewHistoryCard.Locator(".e-headercell").AllInnerTextsAsync();
        return headers.Select(h => h.Trim()).ToList();
    }

    /// <summary>
    /// Returns the text of the given 0-based column index for the Review History grid row at the
    /// given 0-based <paramref name="rowIndex"/> (0 = topmost row — the grid is sorted
    /// newest-first server-side by ReviewDate, with no client-side sorting on top of that). Column
    /// order matches SharedDocumentDetail.razor's Review History GridColumns: 0=Review Date,
    /// 1=Reviewer, 2=Notes, 3=Previous Review Date.
    /// </summary>
    public async Task<string> GetReviewHistoryRowCellAsync(int rowIndex, int columnIndex)
    {
        var row = ReviewHistoryCard.Locator(".e-row").Nth(rowIndex);
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Count of interactive controls (buttons, links, or icon glyphs) rendered anywhere inside the
    /// Review History grid's rows — expected to always be 0, since the grid is strictly read-only
    /// (no edit/delete/action column), unlike the Version History grid immediately above it (which
    /// has a per-row Download link) or other editable grids on this page.
    /// </summary>
    public Task<int> GetReviewHistoryRowActionControlCountAsync() =>
        ReviewHistoryCard.Locator(".e-row button, .e-row a, .e-row i").CountAsync();
}
