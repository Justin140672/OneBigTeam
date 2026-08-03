using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class MyProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile");
        // Poll until the circuit is connected and data loaded: grid visible, no skeleton.
        await page.EvaluateAsync(@"() => {
            window._profileReady = false;
            const poll = setInterval(() => {
                if (!document.querySelector('.overview-skeleton') &&
                    document.querySelector('.overview-grid')) {
                    window._profileReady = true;
                    clearInterval(poll);
                }
            }, 500);
        }");
        await page.WaitForFunctionAsync(
            "window._profileReady === true",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    /// <summary>Waits for the profile page to load after a redirect (e.g. from "View all" link).</summary>
    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(".e-tab", new() { Timeout = 20_000 });
    }

    /// <summary>Returns the name of the currently active tab.</summary>
    public async Task<string> GetActiveTabNameAsync()
    {
        var active = page.Locator("[role='tab'][aria-selected='true']").First;
        await active.WaitForAsync(new() { Timeout = 10_000 });
        return (await active.TextContentAsync())?.Trim() ?? "";
    }

    // ── Profile Photo Header (MyProfilePhotoHeader — self-service) ──────────────
    // Rendered above the tabs on every profile page (not gated on any permission); see
    // MyProfile.razor. Unlike EmployeeEditPage's HR-direct upload, this always creates a
    // pending submission awaiting HR review.

    /// <summary>
    /// Uploads a photo via the self-service "Change Photo" button on the profile page header.
    /// Always goes through the pending-review flow (see UploadMyProfilePhotoAsync on the
    /// backend) — never writes straight to the current photo. Waits for the dialog to close.
    /// </summary>
    public async Task UploadMyProfilePhotoAsync(string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Change Photo" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Change Profile Photo" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Returns true if the "Pending approval" banner is visible on the profile photo header.</summary>
    public Task<bool> HasPendingProfilePhotoBannerAsync() =>
        page.Locator(".alert-warning").Filter(new() { HasText = "Pending approval" }).First.IsVisibleAsync();

    // ── Tab navigation ────────────────────────────────────────────────────────

    public async Task OpenOverviewTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }).ClickAsync();
        await page.WaitForSelectorAsync(".overview-grid, .overview-skeleton, .alert",
            new() { Timeout = 15_000 });
    }

    public async Task OpenContactDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Contact Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".cd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenPersonalDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Personal Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".pd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenEmergencyContactsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Emergency Contacts" }).ClickAsync();
        await page.WaitForSelectorAsync(".ec-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the given document type's row in the (self-service) Document Requests
    /// grid has an "Upload" button — only present while that request's Status is "Requested"
    /// (EmployeeDocumentsTab.razor, EmployeeSelfUpload branch).
    /// </summary>
    public Task<bool> HasUploadButtonForDocumentRequestAsync(string documentTypeName) =>
        DocumentRequestRow(documentTypeName).GetByRole(AriaRole.Button, new() { Name = "Upload" }).IsVisibleAsync();

    /// <summary>
    /// Clicks the "Upload" button on the given document type's row in the self-service Document
    /// Requests grid, fills in the file (the dialog pre-fills Title from the request's document
    /// type), and submits — completing UploadRequestedDocumentDialog.razor's flow. Waits for the
    /// dialog to close, which only happens on a successful upload.
    /// </summary>
    public async Task UploadRequestedDocumentAsync(string documentTypeName, string filePath)
    {
        await DocumentRequestRow(documentTypeName)
            .GetByRole(AriaRole.Button, new() { Name = "Upload" })
            .ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Upload {documentTypeName}" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    private ILocator DocumentRequestRow(string documentTypeName) =>
        page.Locator("[data-testid='admin-document-requests-section'] tbody tr")
            .Filter(new() { HasText = documentTypeName })
            .First;

    // ── Company Documents tab (MyProfileCompanyDocumentsTab — published shared company
    // documents visible to the employee; distinct from the personal "Documents" tab above) ──

    public async Task OpenCompanyDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Company Documents" }).ClickAsync();
        // Renders as a data grid (HrGrid, Title/Category/Effective Date/Acknowledgement columns —
        // "Render Company Documents As Grid" story), not the older icon-card layout. ".e-grid"'s
        // own row selector (or its empty-row/"no documents" text sibling) is the only wait
        // actually tied to the async document fetch having completed, same reasoning as
        // VacancyListPage.RowsRenderedSelector.
        await page.WaitForSelectorAsync(
            ".overview-card, .e-grid .e-row, .e-grid .e-emptyrow, p.text-muted",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Reads the number on one of the four summary tiles at the top of the Company Documents tab
    /// ("Total Available", "Requires Acknowledgement", "Outstanding", "Completed"). Call
    /// <see cref="OpenCompanyDocumentsTabAsync"/> first. Returns -1 if the tile's value can't be
    /// parsed as an integer.
    /// </summary>
    public async Task<int> GetCompanyDocumentsSummaryTileValueAsync(string label)
    {
        var tile = page.Locator(".overview-card.text-center").Filter(new() { HasText = label }).First;
        var text = (await tile.Locator(".fs-4").InnerTextAsync()).Trim();
        return int.TryParse(text, out var value) ? value : -1;
    }

    /// <summary>
    /// Returns the Company Documents grid row matching <paramref name="title"/> (matched against
    /// the Title column's link text — see MyProfileCompanyDocumentsTab.razor's Title GridColumn
    /// Template) — used as the anchor for all of the grid-row helpers below.
    /// </summary>
    private ILocator CompanyDocumentRow(string title) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = title }).First;

    /// <summary>
    /// Returns true if a document row with the given title is currently shown on the Company
    /// Documents tab. Call <see cref="OpenCompanyDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<bool> HasCompanyDocumentCardAsync(string title) =>
        await CompanyDocumentRow(title).CountAsync() > 0 && await CompanyDocumentRow(title).IsVisibleAsync();

    /// <summary>
    /// The acknowledgement-status badge text (e.g. "Acknowledgement Required · Due 28 July 2026"
    /// or "Acknowledged 14 Jul 2026") shown in the grid row's "Acknowledgement" column for the
    /// document matching <paramref name="title"/>, or null if that row currently has no such badge
    /// (the document requires no acknowledgement — see MyProfileCompanyDocumentsTab.razor's
    /// Acknowledgement GridColumn Template, which otherwise renders a plain "—").
    /// </summary>
    public async Task<string?> GetCompanyDocumentAcknowledgementBadgeTextAsync(string title)
    {
        var row = CompanyDocumentRow(title);
        var badge = row.Locator(".badge").Filter(new() { HasText = "Acknowledg" });
        if (await badge.CountAsync() == 0) return null;
        return (await badge.First.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Reads the trimmed text of the Company Documents grid's column headers (expected: Title,
    /// Category, Effective Date, Description, Acknowledgement — see
    /// MyProfileCompanyDocumentsTab.razor's GridColumns). Call
    /// <see cref="OpenCompanyDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCompanyDocumentsGridColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-grid .e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    /// <summary>
    /// Clicks the document row's title link with the given title on the Company Documents tab,
    /// triggering its navigation to the published-document detail page. Call
    /// <see cref="OpenCompanyDocumentsTabAsync"/> first.
    /// </summary>
    public Task ClickCompanyDocumentCardAsync(string title) =>
        CompanyDocumentRow(title).Locator("a").Filter(new() { HasText = title }).First.ClickAsync();

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, p", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns the task titles currently shown in the Tasks tab grid
    /// (src/HR.Web/Components/Pages/Tasks/TaskList.razor). Call <see cref="OpenTasksTabAsync"/>
    /// first. This is the role-agnostic replacement for the old dashboard "My Tasks" widget
    /// (MyTasksWidget.razor), which is no longer rendered anywhere in the app — every employee's
    /// full assigned-task list is only reachable via their own profile Tasks tab now.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var titles = await page.Locator(".task-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Clicks the task row whose title contains <paramref name="titleFragment"/>, opening
    /// TaskViewDialog in place (no navigation). Call <see cref="OpenTasksTabAsync"/> first.
    /// </summary>
    public async Task ClickTaskAsync(string titleFragment)
    {
        await page.Locator(".task-title").Filter(new() { HasText = titleFragment }).First.ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].task-view-dialog", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns the status badge text (e.g. "Open", "In Progress", "Completed") for the task row
    /// whose title contains <paramref name="titleFragment"/>. TaskList.razor has no status filter
    /// — a completed task stays in this grid rather than disappearing, so callers verifying a
    /// completion should assert on this rather than on the task's absence from
    /// <see cref="GetTaskTitlesAsync"/>. Call <see cref="OpenTasksTabAsync"/> first.
    /// </summary>
    public async Task<string> GetTaskStatusAsync(string titleFragment)
    {
        var row = page.Locator(".e-row").Filter(new() { HasText = titleFragment }).First;
        var badge = row.Locator(".task-status-badge");
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        return (await badge.InnerTextAsync()).Trim();
    }

    public async Task OpenAssetsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Assets" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, .spinner-border", new() { Timeout = 15_000 });
        // Wait for the spinner to disappear before asserting grid content.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Returns true if the assets grid has at least one data row.</summary>
    public async Task<bool> HasAssetsTableAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync() > 0;

    /// <summary>Returns all asset numbers from the first column (Asset) in the assets grid.</summary>
    public async Task<IReadOnlyList<string>> GetAssetNumbersAsync()
    {
        var spans = await page.Locator(".e-grid .e-row .e-rowcell:first-child .fw-medium").AllTextContentsAsync();
        return spans.Select(t => t.Trim()).ToList();
    }

    /// <summary>Returns the number of data rows in the assets grid.</summary>
    public async Task<int> GetAssetRowCountAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync();

    // ── Leave tab ─────────────────────────────────────────────────────────────

    public async Task OpenLeaveTabAsync()
    {
        var leaveTab = page.GetByRole(AriaRole.Tab, new() { Name = "Leave" });
        await leaveTab.ClickAsync();
        // Wait for the balance data to load from the API — the Annual Leave card's value must
        // be present before we proceed. Waiting for just .card-header is not enough because
        // that element renders before the async balance fetch completes, leaving _allBalances
        // empty and the leave-type dropdown with no items when the dialog opens.
        await page.Locator(".card").Filter(new() { HasText = "Annual Leave" })
            .Locator("dd.fs-4.fw-semibold")
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Reads the Annual Leave remaining balance from the balance card as a decimal, regardless
    /// of the unit the UI currently renders in. Historically this was "25 days"/"20.5 days"; the
    /// Leave Balance Adjustment feature changed the self-service display to hours (e.g. "112.5h",
    /// no space before the suffix). Rather than depending on either exact format, this extracts
    /// the leading numeric portion so relative before/after comparisons in existing tests
    /// (LeaveApprovalTests, LeaveRejectionTests, LeaveCancellationTests) keep working regardless
    /// of the unit — those tests only assert equality/direction of change, never an absolute
    /// day count, so comparing the hour-denominated value is equally valid for their purposes.
    /// Returns null if the card is not yet visible or the text has no leading number.
    /// </summary>
    public async Task<decimal?> GetAnnualLeaveRemainingAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd   = card.Locator("dd.fs-4.fw-semibold");
        // Wait for the balance to load — it arrives via an async API call after the tab renders.
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        var text = (await dd.TextContentAsync())?.Trim() ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[\d.]+");
        return match.Success && decimal.TryParse(match.Value, out var v) ? v : null;
    }

    /// <summary>
    /// Returns the raw rendered text of the Annual Leave "Remaining" value on the self-service
    /// Leave tab (e.g. "112.5h"), without attempting to parse it to a number. Prefer this over
    /// the (days-format) <see cref="GetAnnualLeaveRemainingAsync"/> helper when asserting on the
    /// hours-based display introduced by the Leave Balance Adjustment feature.
    /// </summary>
    public async Task<string?> GetAnnualLeaveRemainingTextAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd = card.Locator("dd.fs-4.fw-semibold");
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns true if any "Adjust" button is visible anywhere on the self-service Leave tab.
    /// There must never be one here — balance adjustments are an HR/admin-only action performed
    /// from the admin employee edit page, not from an employee's own "My Profile" view.
    /// </summary>
    public async Task<bool> HasAnyAdjustButtonOnLeaveTabAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).CountAsync() > 0;

    /// <summary>Opens the Request Leave dialog.</summary>
    public async Task ClickRequestLeaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Leave" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the status badge text for the leave request row whose text contains
    /// <paramref name="reasonFragment"/>. When a request is rejected the Reason column
    /// switches to the rejection reason, so pass <paramref name="altFragment"/> as the
    /// rejection reason to find the row in that case.
    /// </summary>
    public async Task<string?> GetLeaveRequestStatusAsync(string reasonFragment, string? altFragment = null)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        if (!await row.IsVisibleAsync() && altFragment is not null)
        {
            row = page.Locator("table tbody tr")
                .Filter(new() { HasText = altFragment })
                .First;
        }

        if (!await row.IsVisibleAsync()) return null;

        var statusCell = row.Locator(".badge");
        return (await statusCell.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the rejection reason text for the leave request row matching
    /// <paramref name="reasonFragment"/>, taken from the Reason column.
    /// </summary>
    public async Task<string?> GetLeaveRequestRejectionReasonAsync(string reasonFragment)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        // Reason is in td index 5 (Type, Start, End, Days, Status, Reason, action)
        var cells = await row.Locator("td").AllAsync();
        if (cells.Count < 6) return null;
        return (await cells[5].TextContentAsync())?.Trim();
    }

    // ── Request Leave dialog ───────────────────────────────────────────────────

    public async Task FillLeaveRequestAsync(
        string leaveTypeName,
        string startDate,   // dd/MM/yyyy
        string endDate,     // dd/MM/yyyy
        string? reason = null)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        // Leave Type is the only combobox in this dialog.
        await DropDownSelector.SelectAsync(page, dialog, leaveTypeName);

        // Start date — fill into the first SfDatePicker input.
        var dateInputs = dialog.Locator(".e-date-wrapper input.e-input");
        await dateInputs.Nth(0).ClickAsync();
        await dateInputs.Nth(0).FillAsync(startDate);
        await page.Keyboard.PressAsync("Tab"); // commit the date

        // End date.
        await dateInputs.Nth(1).ClickAsync();
        await dateInputs.Nth(1).FillAsync(endDate);
        await page.Keyboard.PressAsync("Tab");

        // Optional reason.
        if (!string.IsNullOrEmpty(reason))
        {
            var reasonInput = dialog.GetByPlaceholder("Reason for leave request");
            await reasonInput.FillAsync(reason);
        }
    }

    public async Task SubmitLeaveRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        // Dialog closes on success; wait for it to disappear.
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
