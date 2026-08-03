using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the admin employee profile page
/// (/companies/{id}/employees/{employeeId}).
/// Provides access to the Documents and Working Pattern sections.
/// </summary>
public sealed class EmployeeAdminPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        // EmployeeEdit has SfDropDownList components on the Details tab; span[role='combobox']
        // only appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task<string> GetActiveTabNameAsync()
    {
        var active = page.Locator("[role='tab'][aria-selected='true']").First;
        await active.WaitForAsync(new() { Timeout = 10_000 });
        return (await active.TextContentAsync())?.Trim() ?? "";
    }

    // ── Documents tab ─────────────────────────────────────────────────────────

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents", Exact = true }).ClickAsync();
        // Spinner appears while loading, then grid renders
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        // Scoped to the actual Documents grid card specifically (data-testid=
        // "employee-documents-grid-section"), not a bare ".card-body td" — EmployeeDocumentsTab.
        // razor's "Document Requests" card above it (gated by its own, separately-loading
        // _requestsLoading flag) can already have rows rendered while the main Documents card
        // (which holds the "Upload" button) is still loading, making a bare selector resolve
        // against the wrong section. Waiting for the card's own header (present regardless of
        // whether the grid has any rows) rather than a grid-row selector, since an employee with
        // no documents yet would otherwise never satisfy a rows-only wait.
        await page.WaitForSelectorAsync(
            "[data-testid='employee-documents-grid-section'] .card-header",
            new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if any grid cell in the Documents tab contains <paramref name="titleFragment"/>.</summary>
    public async Task<bool> HasDocumentAsync(string titleFragment)
    {
        try
        {
            await page.Locator(".e-gridcontent td, .card-body td")
                .Filter(new() { HasText = titleFragment })
                .First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ── Upload Document dialog (UploadDocumentDialog.razor, admin/EmployeeSelfUpload="false") ──
    // Split into two tabs — "Document Details" (Title/Document Type/Description/Issue Date/Expiry
    // Date) and "File" (the file input) — rather than a single flat form.

    private ILocator UploadDocumentDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });

    /// <summary>Clicks the Documents tab's "Upload" button and waits for the dialog to open.</summary>
    public async Task OpenUploadDocumentDialogAsync()
    {
        // No Exact match — the button's un-hidden FontAwesome <i> icon (no aria-hidden, same as
        // every other icon+text button in this codebase) leaks a leading space into its computed
        // accessible name (confirmed: literally " Upload"), which Exact matching never satisfies.
        // But a bare substring match on "Upload" is ambiguous page-wide — it also matches the
        // profile photo header's unrelated "Upload / Replace Photo" button — so this is scoped to
        // the Documents grid card (data-testid="employee-documents-grid-section") specifically.
        await page.Locator("[data-testid='employee-documents-grid-section']")
            .GetByRole(AriaRole.Button, new() { Name = "Upload" })
            .ClickAsync();
        await UploadDocumentDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>True if the (open) Upload Document dialog shows a "Document Details" tab.</summary>
    public Task<bool> HasUploadDialogDocumentDetailsTabAsync() =>
        UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "Document Details" }).IsVisibleAsync();

    /// <summary>True if the (open) Upload Document dialog shows a "File" tab.</summary>
    public Task<bool> HasUploadDialogFileTabAsync() =>
        UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "File" }).IsVisibleAsync();

    /// <summary>
    /// True if the file input is visible without switching tabs first — expected false, since it
    /// lives on the separate "File" tab, not alongside the "Document Details" fields.
    /// </summary>
    public Task<bool> IsUploadDialogFileInputVisibleAsync() =>
        UploadDocumentDialog.Locator("input[type='file']").IsVisibleAsync();

    public Task FillUploadDialogTitleAsync(string value) =>
        UploadDocumentDialog.GetByPlaceholder("Document title").FillAsync(value);

    public Task SelectUploadDialogDocumentTypeAsync(string typeNameFragment) =>
        DropDownSelector.SelectAsync(page, UploadDocumentDialog, typeNameFragment);

    /// <summary>Switches to the dialog's "File" tab and sets the file to upload.</summary>
    public async Task SelectUploadDialogFileAsync(string filePath)
    {
        await UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "File" }).ClickAsync();
        await UploadDocumentDialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
    }

    /// <summary>Clicks "Upload" and waits for the dialog to close (successful submission).</summary>
    public async Task SubmitUploadDocumentDialogAsync()
    {
        await UploadDocumentDialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await UploadDocumentDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Returns true if the Document Requests section is visible on the Documents tab.</summary>
    public async Task<bool> HasDocumentRequestsSectionAsync() =>
        await page.Locator("[data-testid='admin-document-requests-section']").IsVisibleAsync();

    /// <summary>
    /// Returns true if any row in the Document Requests section contains <paramref name="documentTypeName"/>.
    /// Uses a retrying wait so that data reloaded after an action has time to appear.
    /// </summary>
    public async Task<bool> HasDocumentRequestAsync(string documentTypeName)
    {
        try
        {
            await page.Locator("[data-testid='admin-document-requests-section'] td")
                .Filter(new() { HasText = documentTypeName })
                .First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Returns the status badge text for the first request matching <paramref name="documentTypeName"/>.</summary>
    public async Task<string?> GetDocumentRequestStatusAsync(string documentTypeName)
    {
        var row = page.Locator("[data-testid='admin-document-requests-section'] tr")
            .Filter(new() { HasText = documentTypeName })
            .First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns true if the Request Document button is visible on the Documents tab.</summary>
    public async Task<bool> HasRequestDocumentButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).IsVisibleAsync();

    /// <summary>
    /// Opens the Request Document dialog, selects the given document type, and submits.
    /// Waits for the dialog to close before returning.
    /// </summary>
    public async Task RequestDocumentAsync(string documentTypeName, DateOnly? dueDate = null)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).ClickAsync();

        // Wait for the request-document dialog specifically (avoids matching the grid's column chooser dialog)
        await page.WaitForSelectorAsync(".request-document-dialog", new() { Timeout = 10_000 });

        // Select document type from the Syncfusion dropdown.
        await DropDownSelector.SelectAsync(page, page.Locator(".request-document-dialog"), documentTypeName);

        if (dueDate.HasValue)
        {
            await page.Locator(".request-document-dialog input.e-datepicker").FillAsync(dueDate.Value.ToString("dd/MM/yyyy"));
            await page.Keyboard.PressAsync("Tab");
        }

        await page.Locator(".request-document-dialog").GetByRole(AriaRole.Button, new() { Name = "Request" }).ClickAsync();

        // Wait for dialog to close then for the requests section to reload
        await page.WaitForFunctionAsync(
            "!document.querySelector('.request-document-dialog') || !document.querySelector('.request-document-dialog').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Wait for the document requests section to re-render with updated data
        await page.WaitForSelectorAsync("[data-testid='admin-document-requests-section']", new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Opens the Request Document dialog, selects a document type (without submitting), then
    /// clicks Cancel and confirms "Discard Changes" on the resulting unsaved-changes prompt.
    /// Waits for both dialogs to close. RequestDocumentDialog.razor inherits EditDialogBase,
    /// whose Cancel button routes through an "unsaved changes?" confirmation once any field has
    /// been touched — without UnsavedChangesDialog actually wired up in the markup, that
    /// confirmation had nothing to render and Cancel silently did nothing.
    /// </summary>
    public async Task OpenRequestDocumentDialogSelectTypeThenCancelAsync(string documentTypeName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).ClickAsync();
        await page.WaitForSelectorAsync(".request-document-dialog", new() { Timeout = 10_000 });

        await DropDownSelector.SelectAsync(page, page.Locator(".request-document-dialog"), documentTypeName);

        await page.Locator(".request-document-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        var unsavedDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Unsaved Changes" });
        await unsavedDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await unsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.request-document-dialog') || !document.querySelector('.request-document-dialog').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── Assets tab ────────────────────────────────────────────────────────────

    public async Task OpenAssetsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Assets" }).ClickAsync();
        // Wait for the spinner to disappear, then ensure either the grid or the
        // empty-state placeholder has rendered.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.WaitForSelectorAsync("[data-testid='employee-assets-grid'], .text-muted",
            new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the assets grid has at least one data row.</summary>
    public async Task<bool> HasAssetsGridRowsAsync() =>
        await page.Locator("[data-testid='employee-assets-grid'] .e-row").CountAsync() > 0;

    /// <summary>Returns all asset numbers visible in the first cell of the assets grid.</summary>
    public async Task<IReadOnlyList<string>> GetAssetsGridAssetNumbersAsync()
    {
        var spans = await page
            .Locator("[data-testid='employee-assets-grid'] .e-row .e-rowcell:first-child .fw-medium")
            .AllTextContentsAsync();
        return spans.Select(t => t.Trim()).ToList();
    }

    /// <summary>Returns true if the Assign Asset button is visible on the Assets tab.</summary>
    public async Task<bool> HasAssignAssetButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign Asset" }).IsVisibleAsync();

    /// <summary>Returns true if the Return Asset button is visible on the Assets tab.</summary>
    public async Task<bool> HasReturnAssetButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Return Asset" }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the Return Asset button is currently disabled (no acknowledged asset
    /// is available to return — the button is a standalone action above the grid, not tied to
    /// row selection).
    /// </summary>
    public async Task<bool> IsReturnAssetButtonDisabledAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Return Asset" });
        return await btn.IsDisabledAsync();
    }

    /// <summary>
    /// Opens the Assign Asset dialog and asserts the dialog header is visible.
    /// Does not complete the assignment — use this to verify the dialog opens correctly.
    /// </summary>
    public async Task OpenAssignAssetDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign Asset" }).ClickAsync();
        // The grid keeps a hidden "Choose Columns" dialog (.e-ccdlg) in the DOM at all times,
        // so .e-dialog is ambiguous. Scope to the modal Assign Asset dialog via aria role + name.
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the Assign Asset dialog is currently visible.</summary>
    public async Task<bool> IsAssignAssetDialogVisibleAsync() =>
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" }).IsVisibleAsync();

    /// <summary>
    /// Selects the asset matching <paramref name="assetFragment"/> in the Assign Asset dialog
    /// and clicks Assign. Waits for the dialog to close and the grid to refresh.
    /// Call <see cref="OpenAssignAssetDialogAsync"/> first.
    /// </summary>
    public async Task SelectAssetAndConfirmAsync(string assetFragment)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" });
        await DropDownSelector.SelectAsync(page, dialog, assetFragment);
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Dismisses the Assign Asset dialog by clicking Cancel.</summary>
    public async Task CloseAssignAssetDialogAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Leave tab ─────────────────────────────────────────────────────────────

    public async Task OpenLeaveTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Leave" }).ClickAsync();
        // Wait for the spinner to disappear, then for at least one balance value/"n/a" span
        // to render inside the "Current Balance" card — not just the static card shell that
        // is present before the async balance fetch completes.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.Locator(".card-body .fs-4.fw-semibold").First.WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns the rendered balance text (e.g. "112.5h" or "n/a") for the given leave type's
    /// row in the "Current Balance" card. Does not cover the TOIL card — use
    /// <see cref="GetToilBalanceTextAsync"/> for that.
    /// </summary>
    public async Task<string?> GetBalanceRowTextAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        var value = row.Locator(".fs-4.fw-semibold");
        await value.WaitForAsync(new() { Timeout = 15_000 });
        return (await value.TextContentAsync())?.Trim();
    }

    /// <summary>Returns true if an Adjust button is visible on the given leave type's row
    /// in the "Current Balance" card.</summary>
    public async Task<bool> HasAdjustButtonAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        return await row.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).IsVisibleAsync();
    }

    /// <summary>Returns the rendered balance text (e.g. "20h" or "n/a") for the TOIL Balance card.</summary>
    public async Task<string?> GetToilBalanceTextAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        var value = card.Locator(".fs-4.fw-semibold");
        await value.WaitForAsync(new() { Timeout = 15_000 });
        return (await value.TextContentAsync())?.Trim();
    }

    /// <summary>Returns true if the Adjust button is visible on the TOIL Balance card.</summary>
    public async Task<bool> HasToilAdjustButtonAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        return await card.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the Adjust button on the given leave type's row in the "Current Balance" card
    /// and waits for the AdjustLeaveBalanceDialog (header "Adjust {leaveTypeName} Balance") to open.
    /// </summary>
    public async Task OpenAdjustDialogAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        await row.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Clicks the Adjust button on the TOIL Balance card and waits for its dialog to open.
    /// <paramref name="toilLeaveTypeName"/> must match the leave type's actual Name (e.g.
    /// "Time Off In Lieu"), since that — not the "TOIL" card label — is what the dialog
    /// header renders.
    /// </summary>
    public async Task OpenToilAdjustDialogAsync(string toilLeaveTypeName = "Time Off In Lieu")
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {toilLeaveTypeName} Balance" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>Returns true if the Adjust dialog for the given leave type is currently visible.</summary>
    public async Task<bool> IsAdjustDialogVisibleAsync(string leaveTypeName) =>
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" }).IsVisibleAsync();

    /// <summary>Fills the numeric hours field of the (already open) Adjust dialog for
    /// <paramref name="leaveTypeName"/> without submitting — used by cancel-behavior tests.</summary>
    public Task FillAdjustmentAmountAsync(string leaveTypeName, decimal hours) =>
        FillAdjustmentFormAsync(
            page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" }),
            hours, reason: null, comments: null, allowNegativeOverride: false);

    /// <summary>
    /// Fills and submits the Adjust Leave Balance dialog for the given leave type. Does not
    /// assume success — after this returns, callers should check
    /// <see cref="IsAdjustDialogVisibleAsync"/> (still open on failure) and/or
    /// <see cref="GetAdjustDialogErrorAsync"/> to determine the outcome.
    /// </summary>
    public async Task SubmitAdjustmentAsync(
        string leaveTypeName,
        decimal? hours,
        string? reason = null,
        string? comments = null,
        bool allowNegativeOverride = false)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        await FillAdjustmentFormAsync(dialog, hours, reason, comments, allowNegativeOverride);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // The save is an async API round-trip. On success the dialog closes; on failure an
        // inline .alert-danger renders while the dialog stays open. Wait for the dialog to
        // close first; if it doesn't (validation failure), wait for the error to actually render
        // before returning control to the caller.
        try
        {
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    private async Task FillAdjustmentFormAsync(
        ILocator dialog, decimal? hours, string? reason, string? comments, bool allowNegativeOverride)
    {
        if (hours.HasValue)
        {
            var hoursInput = dialog.Locator("input.e-numerictextbox");
            await hoursInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await hoursInput.PressSequentiallyAsync(hours.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await page.Keyboard.PressAsync("Tab");
        }

        if (!string.IsNullOrEmpty(reason))
        {
            await DropDownSelector.SelectAsync(page, dialog, reason);
        }

        if (!string.IsNullOrEmpty(comments))
            await dialog.Locator("textarea").FillAsync(comments);

        if (allowNegativeOverride)
            await dialog.Locator("#allowNegativeOverride").CheckAsync();
    }

    /// <summary>Clicks Cancel on the given leave type's Adjust dialog and waits for it to close.</summary>
    public async Task CloseAdjustDialogAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Returns the inline validation error text in the currently-open Adjust dialog for
    /// <paramref name="leaveTypeName"/>, or null if no error is visible.</summary>
    public async Task<string?> GetAdjustDialogErrorAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        var error = dialog.Locator(".alert-danger");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the trimmed text of the "Adjustment (days)"/"Adjustment (hours)" field label in
    /// the currently-open Adjust dialog for <paramref name="leaveTypeName"/> — used to assert
    /// the dialog's unit wording (days for a Standard-behaviour leave type, hours for TOIL).
    /// </summary>
    public async Task<string?> GetAdjustmentLabelTextAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        var label = dialog.Locator("label.form-label").Filter(new() { HasText = "Adjustment (" });
        await label.WaitForAsync(new() { Timeout = 10_000 });
        return (await label.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the trimmed text of the read-only "Current Balance" field inside the currently-open
    /// Adjust dialog for <paramref name="leaveTypeName"/> (e.g. "25 days" or "20h") — distinct from
    /// <see cref="GetBalanceRowTextAsync"/>/<see cref="GetToilBalanceTextAsync"/>, which read the
    /// balance card on the main Leave tab rather than the dialog itself.
    /// </summary>
    public async Task<string?> GetAdjustDialogCurrentBalanceTextAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        // AdjustLeaveBalanceDialog.razor's "Reason" SfDropDownList also renders a hidden
        // readonly input under the hood, so a bare "input[readonly]" matches both it and the
        // intended Current Balance field. Scope to ".form-control" (the Current Balance input's
        // own class; the dropdown's internal input uses Syncfusion's "e-control"/"e-input"
        // classes instead) to disambiguate.
        var input = dialog.Locator("input.form-control[readonly]");
        return (await input.InputValueAsync())?.Trim();
    }

    // ── Employment tab ────────────────────────────────────────────────────────

    public async Task OpenEmploymentTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Employment" }).ClickAsync();
        // Wait for the Employment-tab-specific heading — the generic .card-header selector
        // would resolve immediately against the Details tab's already-rendered card headers.
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });
    }

    // ── Working Pattern section (Employment tab) ──────────────────────────────

    /// <summary>
    /// Ensures the working pattern override is active — i.e. unchecks "Use company working
    /// pattern" so the Working Days / Hours Per Day fields render. Call <see cref="OpenEmploymentTabAsync"/> first.
    /// </summary>
    public async Task EnableWorkingPatternOverrideAsync()
    {
        // Syncfusion puts class e-numerictextbox ON the <input> itself; the Hours Per Day
        // field is only rendered while the "Use company working pattern" checkbox is unchecked.
        // If a previous test run already saved the override, the input is already visible —
        // don't click again or we would toggle it back on.
        var numericInput = page.Locator("input.e-numerictextbox");
        if (!await numericInput.IsVisibleAsync())
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Use company working pattern" });
            await wrapper.Locator("label").ClickAsync();
            await numericInput.WaitForAsync(new() { Timeout = 10_000 });
        }
    }

    /// <summary>
    /// Sets the Hours Per Day numeric field in the Working Pattern section.
    /// Assumes the override is already enabled.
    /// </summary>
    public async Task SetHoursPerDayAsync(decimal hours)
    {
        var input = page.Locator("input.e-numerictextbox").First;
        await input.FillAsync(hours.ToString("0.#"));
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates to /employees list on success
        await page.WaitForURLAsync("**/employees", new() { Timeout = 15_000 });
    }
}
