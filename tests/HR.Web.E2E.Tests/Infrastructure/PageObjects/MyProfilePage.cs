using HR.Web.E2E.Tests.Infrastructure;
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
    // Bumped from the 5s default — photo upload + pending-review-task creation is a heavier
    // server round-trip (file write + task/notification creation) than most banner checks in this
    // suite, and can exceed 5s under load.
    public Task<bool> HasPendingProfilePhotoBannerAsync() =>
        page.Locator(".alert-warning").Filter(new() { HasText = "Pending approval" }).First.WaitUntilVisibleAsync(15_000);

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

    /// <summary>
    /// Opens the (merged) "Documents" tab — MyProfileDocumentsTab.razor — which now shows a
    /// single grid combining the employee's personal documents AND published company-wide
    /// documents (each row tagged via its "Source" column), replacing what used to be two
    /// separate tabs ("Documents" and "Company Documents"). ".e-grid"'s own row selector (or its
    /// empty-row/"no documents" text sibling) is the only wait actually tied to the async document
    /// fetch having completed — the grid card mounts before Syncfusion populates its
    /// .e-row/.e-headercell DOM on a separate JS tick, so waiting on a bare ".card" (as before)
    /// could resolve before the grid had actually finished rendering.
    /// </summary>
    // ── Equality & Diversity tab (MyProfileEqualityDiversityTab — self-service) ──
    // Tab index 4, immediately after "Emergency Contacts", before "Leave". The section
    // container carries data-testid="my-profile-equality-section"; drive its fields with the
    // EqualityDiversityTab page object.

    public async Task OpenEqualityDiversityTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Equality & Diversity" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='my-profile-equality-section'], .ed-section, .alert-danger",
            new() { Timeout = 15_000 });
    }

    public async Task OpenEqualityGenderAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-gender']"), optionText);

    public async Task OpenEqualityMaritalAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-marital']"), optionText);

    public async Task OpenEqualityEthnicGroupAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-ethnicgroup']"), optionText);

    public async Task OpenEqualityDisabilityAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-disability']"), optionText);

    public async Task OpenEqualityOrientationAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-orientation']"), optionText);

    public async Task OpenEqualityReligionAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-religion']"), optionText);

    public async Task<string> GetEqualitySelectedValueAsync(string fieldTestId) =>
        (await page.Locator($"[data-testid='{fieldTestId}'] span[role='combobox'] input").First.InputValueAsync())?.Trim() ?? "";

    public async Task SaveEqualityAsync()
    {
        await page.Locator("[data-testid='my-profile-equality-save']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']", new() { Timeout = 15_000 });
    }

    public async Task ClearEqualityAnswersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear my answers" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']", new() { Timeout = 15_000 });
    }

    public async Task<bool> IsEqualitySuccessBannerVisibleAsync() =>
        await page.Locator("[data-testid='my-profile-equality-success']").IsVisibleAsync();

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='my-profile-documents-grid-section'] .e-grid .e-row, " +
            "[data-testid='my-profile-documents-grid-section'] .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the given document type's row in the (self-service) Document Requests
    /// section has an "Upload" button — only present while that request's Status is "Requested"
    /// (MyProfileDocumentsTab.razor's own Document Requests table, data-testid=
    /// "my-profile-document-requests-section" — distinct from EmployeeDocumentsTab.razor's admin
    /// equivalent, "admin-document-requests-section").
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
        page.Locator("[data-testid='my-profile-document-requests-section'] tbody tr")
            .Filter(new() { HasText = documentTypeName })
            .First;

    // ── Documents grid rows (MyProfileDocumentsTab — merged personal + company documents) ──

    /// <summary>
    /// Returns the merged Documents grid row matching <paramref name="title"/> (matched against
    /// the Title column's text — see MyProfileDocumentsTab.razor's Title GridColumn Template,
    /// which renders company-sourced rows as a link and personal rows as plain text) — used as the
    /// anchor for all of the grid-row helpers below. Call <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    private ILocator DocumentRow(string title) =>
        page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid .e-row")
            .Filter(new() { HasText = title })
            .First;

    /// <summary>
    /// Returns true if a document row with the given title is currently shown in the merged
    /// Documents grid, regardless of source. Call <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<bool> HasDocumentRowAsync(string title) =>
        await DocumentRow(title).CountAsync() > 0 && await DocumentRow(title).IsVisibleAsync();

    /// <summary>
    /// Returns the "Source" column's badge text ("Personal" or "Company") for the document row
    /// matching <paramref name="title"/> — see MyProfileDocumentRow.Source /
    /// MyProfileDocumentsTab.razor's Source GridColumn Template. Call
    /// <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<string> GetDocumentRowSourceAsync(string title)
    {
        var row = DocumentRow(title);
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // The Source column is the second grid column, always rendered as a single badge — unlike
        // the Status column (badge text varies/absent), there's exactly one badge to match here.
        var badge = row.Locator(".badge").First;
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        return (await badge.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// The Status column's badge text for the document row matching <paramref name="title"/> —
    /// e.g. "Active"/"Expiring Soon" for a personal document, or "Acknowledgement Required"/
    /// "Acknowledged 14 Jul 2026"/"No acknowledgement required" for a company document (see
    /// MyProfileDocumentsTab.razor's GetPersonalStatusBadge/GetCompanyStatusBadge). Call
    /// <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<string?> GetDocumentRowStatusTextAsync(string title)
    {
        var row = DocumentRow(title);
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // Two badges exist per row (Source, then Status) — Status is always the second/last one,
        // since every row (personal or company) always renders exactly one Status badge, unlike
        // the old Company Documents tab's Acknowledgement column which could render a plain "—"
        // placeholder instead of a badge.
        var badges = row.Locator(".badge");
        await badges.Last.WaitForAsync(new() { Timeout = 15_000 });
        var count = await badges.CountAsync();
        return count >= 2 ? (await badges.Nth(count - 1).InnerTextAsync()).Trim() : null;
    }

    /// <summary>
    /// Reads the trimmed text of the merged Documents grid's column headers (expected: Title,
    /// Source, Type / Category, Date, Status — see MyProfileDocumentsTab.razor's GridColumns).
    /// Call <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDocumentsGridColumnHeadersAsync()
    {
        // See OpenDocumentsTabAsync's doc comment — its own row/empty-state wait doesn't guarantee
        // the header cells have finished their own separate Syncfusion JS render pass, so an
        // instant AllAsync() here can read zero headers. Wait for at least one before reading.
        var headerCell = page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid .e-headercell");
        await headerCell.First.WaitForAsync(new() { Timeout = 15_000 });

        var headers = await headerCell.AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    /// <summary>
    /// Clicks the document row's title link with the given title in the merged Documents grid —
    /// only meaningful for a company-sourced row (personal rows render plain, unlinked text — see
    /// MyProfileDocumentsTab.razor's Title GridColumn Template) — triggering navigation to the
    /// published-document detail page. Call <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public Task ClickDocumentTitleLinkAsync(string title) =>
        DocumentRow(title).Locator("a").Filter(new() { HasText = title }).First.ClickAsync();

    /// <summary>
    /// Clicks the row's Actions-column button for the document matching <paramref name="title"/> —
    /// "Download" for a personal row, "View" for a company row (see MyProfileDocumentsTab.razor's
    /// final GridColumn Template). Call <see cref="OpenDocumentsTabAsync"/> first.
    /// </summary>
    public Task ClickDocumentRowActionAsync(string title, string actionName) =>
        DocumentRow(title).GetByRole(AriaRole.Button, new() { Name = actionName }).ClickAsync();

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();

        // Scoped to the active tab item's own row/empty-state, not just any ".e-grid" on the page —
        // Syncfusion's SfTab keeps other tab panels' content mounted (hidden, not destroyed), so a
        // grid left over from a previously-visited tab (e.g. Leave) can already satisfy a bare
        // ".e-grid" selector before MyProfileTasksTab's own async load (TaskService.GetMyTasksAsync)
        // has actually finished and rendered this tab's rows.
        await page.WaitForSelectorAsync(
            ".e-tab .e-item.e-active .e-grid .e-row, .e-tab .e-item.e-active .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
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
        var row = page.Locator(".task-title").Filter(new() { HasText = titleFragment }).First;
        // OpenTasksTabAsync's own wait only proves *some* row/empty-row rendered, not that this
        // specific task's row has landed yet — a task created moments earlier (e.g. by a document
        // request on a different page/login) can still be a tick behind the grid's initial
        // render. Without an explicit wait here, a locator matching zero elements falls through to
        // Playwright's default 30s actionability wait on ClickAsync with no useful diagnostic.
        await row.WaitForAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
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

        // The ".e-grid" container mounts before Syncfusion populates its ".e-row"/".e-rowcell"
        // data on a separate JS tick (same race documented on EmployeeEditPage.SaveNewEmployeeAsync
        // and EmployeeAdminPage.OpenAssetsTabAsync) — a caller that immediately checks
        // HasAssetsTableAsync/GetAssetRowCountAsync can otherwise see zero rows even for an
        // employee with seeded assets. Only wait when a grid actually rendered (not the
        // empty-state placeholder, which never has ".e-grid" at all).
        if (await page.Locator(".e-grid").First.IsVisibleAsync())
        {
            await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow",
                new() { Timeout = 15_000 });
        }
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

        // Callers typically only wait for *some* "table tbody tr" to exist after submitting a
        // request (proving the table itself has rendered), not specifically for this new,
        // uniquely-reasoned row to have appeared among them — the table can still be mid-reload a
        // tick later, same "container before content" race fixed elsewhere on the asset/task
        // grids. A bare instant IsVisibleAsync() here raced that and returned null under load
        // instead of waiting for the row to actually show up.
        try
        {
            await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException) when (altFragment is not null)
        {
            // Fall through to the alt-fragment lookup below.
        }

        if (!await row.IsVisibleAsync() && altFragment is not null)
        {
            row = page.Locator("table tbody tr")
                .Filter(new() { HasText = altFragment })
                .First;
            try
            {
                await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                // Neither fragment matched a row in time — fall through to the null return below.
            }
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
        // Wait for the Blazor round-trip that commits the date into component state
        // (ValueChanged fires after blur) before touching the next field — otherwise
        // a fast test runner can click Submit before _startDateSet flips to true.
        await Assertions.Expect(dateInputs.Nth(0)).ToHaveValueAsync(startDate);

        // End date.
        await dateInputs.Nth(1).ClickAsync();
        await dateInputs.Nth(1).FillAsync(endDate);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(dateInputs.Nth(1)).ToHaveValueAsync(endDate);

        // Optional reason.
        if (!string.IsNullOrEmpty(reason))
        {
            var reasonInput = dialog.GetByPlaceholder("Reason for leave request");
            await reasonInput.FillAsync(reason);
            await page.Keyboard.PressAsync("Tab");
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
