using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee edit/create page.
/// Covers the new-employee form and the Employment tab of existing employees.
/// Routes: /companies/{id}/employees/new  and  /companies/{id}/employees/{id}
/// </summary>
public sealed class EmployeeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/new");
        // Wait for Syncfusion to initialise — span[role='combobox'] only appears after
        // Blazor's interactive render, ensuring the form's event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        // span[role='combobox'] (SfDropDownList) only appears after Blazor's interactive
        // render, confirming the circuit is connected and event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Navigates directly to the employee's read-only "/view" route — the same place
    /// ClickEmployeeAsync's row link lands on, without going through the employee list. Useful
    /// for tests that just need to (re)land in view mode on a specific employee, rather than
    /// recreating one via the full New Employee form each time.
    /// </summary>
    public async Task GoToViewAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/view");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Navigates directly to the employee edit page with a query string appended (e.g.
    /// "tab=onboarding") — used to verify deep-link tab activation (see EmployeeEdit.razor's
    /// LoadAsync, which maps the "tab" query parameter to an initial SfTab selected index).
    /// </summary>
    public async Task GoToAsync(Guid companyId, Guid employeeId, string query)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}?{query}");
        // Deep-linking can land on any tab, and not every tab renders a combobox (Onboarding and
        // Offboarding don't), so — unlike the other GoToAsync overloads above, which always land
        // on "Details" (which does) — wait on the tab list itself instead. It's rendered by the
        // same SfTab regardless of which tab query-string selects, so it's still a reliable
        // signal that Blazor's interactive circuit has connected.
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    // ── New Employee — Personal Information ───────────────────────────────────

    public async Task FillFirstNameAsync(string value)
    {
        await page.GetByPlaceholder("First name", new() { Exact = true }).FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillLastNameAsync(string value)
    {
        await page.GetByPlaceholder("Last name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillWorkEmailAsync(string value)
    {
        await page.GetByPlaceholder("work@company.com").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillStartDateAsync(string ddMMyyyy)
    {
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        // Date of Birth renders before Start Date on the new employee form (EmployeeEdit.razor) —
        // the start date picker is the second date input, not the first.
        await inputs.Nth(1).ClickAsync();
        await inputs.Nth(1).FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDateOfBirthAsync(string ddMMyyyy)
    {
        // Date of birth is the first date picker on the new employee form.
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        await inputs.First.ClickAsync();
        await inputs.First.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Selects a value from a Syncfusion SfDropDownList identified by nearby label text.</summary>
    public Task SelectDropdownAsync(string labelText, string optionText) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-6, .col-md-4").Filter(new() { HasText = labelText }).First, optionText);

    // ── Employee Overview header ────────────────────────────────────────────────

    /// <summary>
    /// Returns the text of the employee status badge shown next to the employee's name at the
    /// top of the page (e.g. "Active", "Leaving", "Former Employee" — see EmployeeEdit.razor's
    /// StatusDisplayName). Scoped to the "rounded-pill" class combo distinguishing it from the
    /// Reporting Chain card's own "Current Employee" badge and every lifecycle tab's own status
    /// badge further down the page — see the Playwright locator conventions around bare-class
    /// locators for why a plain ".badge" alone would be ambiguous here.
    /// </summary>
    public async Task<string?> GetEmployeeStatusBadgeTextAsync()
    {
        var badge = page.Locator(".badge.rounded-pill").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    // ── View mode / Edit mode (2026-08 profile redesign) ────────────────────────
    // Existing employees now open read-only at ".../{Id}/view"; "Edit details" drops the
    // suffix and reloads into the editable route. See EditPageBase.IsViewMode (URL-derived)
    // and EmployeeEdit.razor's EnterEditMode/CancelEdit.

    public bool IsInViewModeUrl => page.Url.Contains("/view", StringComparison.OrdinalIgnoreCase);

    public Task<bool> IsEditDetailsButtonVisibleAsync() =>
        page.Locator("[data-testid='edit-details-button']").IsVisibleAsync();

    /// <summary>Clicks "Edit details" and waits for the resulting forceLoad reload to land on the editable route.</summary>
    public async Task ClickEditDetailsButtonAsync()
    {
        var button = page.Locator("[data-testid='edit-details-button']");

        // The button only renders for IsViewMode && Session.CanManageEmployees (EmployeeEdit.razor)
        // — a bare ClickAsync() here relies entirely on Playwright's own 30s default actionability
        // timeout and gives no indication, on failure, of WHY it never appeared (still on the edit
        // route because a prior navigation didn't land where expected, vs. a real rendering delay
        // under load, vs. a permissions gap). Surface that distinction instead of a bare timeout.
        try
        {
            await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            throw new Exception(
                "Timed out waiting for the 'Edit details' button to appear. " +
                $"Current URL: {page.Url} (IsViewMode expected — url should contain '/view'). " +
                "The button only renders when IsViewMode is true and the caller can manage employees — " +
                "check whether navigation actually landed on the view route.");
        }

        await button.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/view", StringComparison.OrdinalIgnoreCase), new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public Task<bool> IsBackToEmployeesButtonVisibleAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Back to employees" }).IsVisibleAsync();

    public async Task ClickBackToEmployeesButtonAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Back to employees" }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
    }

    /// <summary>The sticky Save/Cancel action bar (".employee-edit-sticky-bar") — only rendered in edit mode.</summary>
    public Task<bool> IsStickyActionBarVisibleAsync() =>
        page.Locator(".employee-edit-sticky-bar").IsVisibleAsync();

    /// <summary>
    /// Returns the accessible success confirmation banner's text (role="status" aria-live="polite",
    /// shown for ~700ms after a successful save before the redirect navigates away — see
    /// EmployeeEdit.razor's OnSavedAsync), or null if not currently visible.
    /// </summary>
    public async Task<string?> GetSaveSuccessBannerTextAsync()
    {
        var banner = page.Locator("[role='status'][aria-live='polite'].alert-success");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    // ── "More actions" dropdown (Organisation Chart / Start offboarding) ───────

    public Task<bool> IsMoreActionsMenuVisibleAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "More actions" }).IsVisibleAsync();

    public Task OpenMoreActionsMenuAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();

    public async Task ClickViewOrganisationChartMenuItemAsync()
    {
        await OpenMoreActionsMenuAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "View Organisation Chart" }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/organisation-chart\?employeeId="), new() { Timeout = 15_000 });
    }

    /// <summary>
    /// True if the "Start offboarding" item is present in the (currently closed) "More actions"
    /// menu — only rendered while no leaving process is active (see EmployeeEdit.razor's
    /// BuildMoreActionsItems / `!_showLeavingTab`). Opens the menu to check, then closes it again
    /// via Escape so callers aren't left with an open popup. Replaces the old header
    /// "Start Leaving Process" button check now that the action lives in this overflow menu and
    /// is labelled "Start offboarding".
    /// </summary>
    public async Task<bool> HasStartOffboardingMenuItemAsync()
    {
        if (!await IsMoreActionsMenuVisibleAsync())
            return false;

        await OpenMoreActionsMenuAsync();
        bool visible;
        try
        {
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Start offboarding" })
                .WaitForAsync(new() { Timeout = 3_000 });
            visible = true;
        }
        catch (TimeoutException)
        {
            visible = false;
        }

        await page.Keyboard.PressAsync("Escape");
        return visible;
    }

    public async Task ClickStartOffboardingMenuItemAsync()
    {
        await OpenMoreActionsMenuAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Start offboarding" }).ClickAsync();
    }

    // ── Details tab field access (view-mode read-only checks / accessible labels) ─

    /// <summary>
    /// True if the given Details-tab text input (by its accessible label/aria-label, e.g. "First
    /// Name") carries the HTML `readonly` attribute. Located by label rather than `id` — Syncfusion
    /// always overwrites any custom `id` passed to HrTextBox with its own auto-generated one (see
    /// HrTextBox's own remarks / EmployeeEdit.razor's HtmlAttributes["aria-label"] fields), so an
    /// id-based CSS selector never reliably resolves.
    /// </summary>
    public async Task<bool> IsTextFieldReadOnlyAsync(string fieldLabel) =>
        await page.GetByLabel(fieldLabel).First.GetAttributeAsync("readonly") is not null;

    public Task<string> GetTextFieldValueAsync(string fieldLabel) =>
        page.GetByLabel(fieldLabel).First.InputValueAsync();

    public Task FillTextFieldByIdAsync(string fieldLabel, string value) =>
        page.GetByLabel(fieldLabel).First.FillAsync(value);

    /// <summary>True if the "Fields marked * are required." explanatory note is visible on the Details tab.</summary>
    public async Task<bool> HasRequiredFieldsNoteAsync()
    {
        // ClickEditDetailsButtonAsync's own wait only confirms SOME combobox rendered somewhere on
        // the page, not specifically the Details tab's own content — this note can still be a
        // render pass behind that at the moment a caller checks immediately afterward. Poll
        // briefly rather than a single snapshot.
        var note = page.Locator("p").Filter(new() { HasText = "Fields marked" }).Filter(new() { HasText = "are required" }).First;
        try
        {
            await note.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// A bare IsVisibleAsync() snapshot here can fire before the card has actually rendered —
    /// same "container mounts before Blazor content renders" race documented across other page
    /// objects in this suite. Previously masked by the incidental delay of each caller's own full
    /// New Employee form creation flow; surfaced once callers started reaching this page via a
    /// much faster shared-employee navigation instead. Use an auto-retrying wait rather than a
    /// one-shot check.
    /// </summary>
    public async Task<bool> IsUsersAndAccessCardVisibleAsync()
    {
        try
        {
            await Assertions.Expect(page.Locator(".card-header:has-text('Users & Access')").First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public Task<bool> HasInviteExpiryNoteAsync() =>
        page.Locator("p").Filter(new() { HasText = "Invite links expire after 7 days" }).First.IsVisibleAsync();

    // ── Employment Tab ─────────────────────────────────────────────────────────

    public async Task OpenEmploymentTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Employment" }).ClickAsync();
        // Wait for the Employment-tab-specific heading — the generic .card-header selector
        // would resolve immediately against the Details tab's already-rendered card headers.
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });

        // The heading above is on the FIRST card of this tab; every combobox further down still
        // needs Syncfusion's JS interop to attach before it's genuinely click-ready, and the very
        // first popup opened on a freshly-loaded page pays a further, much larger one-time
        // cold-start cost on top of that. DropDownSelector itself detects and sizes for both
        // automatically (see its own remarks) — but that just means the FIRST dropdown any caller
        // happens to touch on this tab pays the cold cost, whichever one it is (confirmed: this
        // tab's Position Profile field hitting it in CreateEmployeeTests, after previously being
        // reliable, once this warm-up was removed during an earlier consolidation pass). Paying
        // that cost once, right here, up front, means every real selection a caller makes
        // afterward — Manager, Position Profile, Department, whatever order the test uses — lands
        // on the fast "warm" path instead of each independently risking being the unlucky first
        // one. Manager is the natural choice: last in DOM order, so warming it up implies every
        // earlier combobox on this tab already had time to attach too.
        // A plain click + Escape, not DropDownSelector.SelectAsync — that method deliberately
        // no-ops when the combobox's current value already matches the target text (correct for
        // real selections, wrong here: a brand-new employee's Manager already reads "No Manager",
        // which is exactly the common case this warm-up most needs to cover, so skipping on
        // already-matching text would skip the warm-up for it entirely).
        var managerCombobox = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First
            .Locator("span[role='combobox']")
            .First;
        try
        {
            await managerCombobox.ClickAsync(new() { Timeout = 60_000 });
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
        catch (TimeoutException)
        {
            // Best-effort warm-up only — if this doesn't even open, the caller's own real
            // DropDownSelector.SelectAsync call will surface the actual failure with its own
            // (equally cold-start-aware) retry logic.
        }
    }

    /// <summary>
    /// Reads the plain-text value of the "Current Compensation" card's read-only "Current
    /// Salary"/"Hours"/"FTE"/"Effective From" rows (see EmployeeEmploymentTab.razor's
    /// CurrentSalaryDisplay/CurrentHoursDisplay/CurrentFteDisplay/CurrentEffectiveFromDisplay) —
    /// rendered as a plain &lt;table&gt; of &lt;th&gt;/&lt;td&gt; rows, not the form-control-plaintext
    /// fields used elsewhere on this page.
    /// </summary>
    public async Task<string?> GetEmploymentTabReadOnlyFieldAsync(string labelText)
    {
        var row = page.Locator("table.table-sm tr").Filter(new() { HasText = labelText }).First;
        if (await row.CountAsync() == 0) return null;
        var value = row.Locator("td").First;
        return await value.IsVisibleAsync() ? (await value.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the trimmed card-header headings in DOM order on the (currently open) Employment
    /// tab — used to assert the "Organisation" card renders above the "Dates" card.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEmploymentTabCardHeadingsAsync()
    {
        var headers = await page.Locator(".card-header h5").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    /// <summary>
    /// Selects a manager from the Manager dropdown on the Employment tab. DropDownSelector itself
    /// confirms Blazor's ValueChanged round-trip actually committed the selection before
    /// returning — see its own doc comment.
    /// </summary>
    public async Task SelectManagerAsync(string managerNameFragment)
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, managerNameFragment);
    }

    /// <summary>
    /// Clears the manager selection on the Employment tab by opening the Manager dropdown and
    /// selecting its prepended "No Manager" sentinel item (Id = Guid.Empty) — replaces the old
    /// ShowClearButton ("x" icon) approach, which was removed in favor of this explicit
    /// no-selection list item (see EmployeeEmploymentTab.razor's ManagerOption list, which
    /// prepends a Guid.Empty/"No Manager" entry rather than setting ShowClearButton="true").
    /// </summary>
    public async Task ClearManagerAsync()
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, "No Manager");
    }

    /// <summary>
    /// Clicks the single page-level Save button (persistent across all tabs, below the SfTab).
    /// Saves both the Details and Employment tabs together and navigates to the employee list on success.
    /// </summary>
    /// <remarks>
    /// Previously this only waited for the spinner to clear, which is true whether the save
    /// succeeded OR failed validation (e.g. EmployeeEmploymentTab.SaveCoreAsync's
    /// EditContext.Validate() failing, or UpdateEmploymentDetailsHandler returning a Conflict) —
    /// a failed save leaves the caller on the same edit page with stale/unsaved field values and
    /// no exception, so any later assertion about what got saved fails far from the actual cause.
    /// Explicitly checking for the error banner here turns that into an immediate, specific failure.
    /// </remarks>
    public async Task ClickSaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();

        var errorBanner = page.Locator(".alert-danger").First;
        if (await errorBanner.IsVisibleAsync())
        {
            var message = (await errorBanner.TextContentAsync())?.Trim();
            throw new Exception($"Save failed: {message}");
        }
    }

    // ── Save (new employee form) ───────────────────────────────────────────────

    public async Task SaveNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // A failed save (client validation or a server-side Conflict/error) leaves this on the same
        // form with no navigation — waiting on the URL alone then surfaces as a generic 40s timeout
        // with no indication of the real cause, unlike ClickSaveChangesAsync above (which already
        // checks this). Give the spinner a moment to clear and check for an error banner before
        // committing to the long navigation wait, so a genuine validation/server failure fails fast
        // and loud instead of masquerading as a load-related timeout.
        try
        {
            await page.WaitForSelectorAsync(".alert-danger", new() { Timeout = 2_000 });
            var message = (await page.Locator(".alert-danger").First.TextContentAsync())?.Trim();
            throw new Exception($"Save failed: {message}");
        }
        catch (TimeoutException)
        {
            // No error banner appeared — proceed to the normal success-path wait below.
        }

        // Navigates to the employee list on success. Bumped 20s -> 40s -> 60s: under the higher
        // concurrent load from the many tests that now create fresh employees via this same
        // full-form UI flow, this genuinely (not a logic bug — flow is identical to the
        // long-established working pattern) takes longer than the previous budget often enough
        // to time out. Same pattern as EmployeeListPage.ClickNewEmployeeAsync's navigation wait.
        await page.WaitForURLAsync("**/employees", new() { Timeout = 60_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid. ".e-grid"
        // alone isn't enough — Syncfusion populates ".e-row"/".e-rowcell" on a separate JS tick
        // after the grid element mounts, so callers that immediately click the new row (e.g.
        // ManagerDashboardTests.CreateEmployeeReportingToDavidAsync) can race an empty grid.
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasProbationSummaryAsync() =>
        await page.Locator("[data-testid='probation-summary']").IsVisibleAsync();

    /// <summary>
    /// Returns true if the "From Position Profile" read-only defaults summary card is visible
    /// on the new-employee form (shown after a Position Profile is selected).
    /// </summary>
    public async Task<bool> HasPositionProfileDefaultsSummaryAsync() =>
        await page.Locator("[data-testid='position-profile-defaults-summary']").IsVisibleAsync();

    /// <summary>Reads the current value of the Department dropdown's visible text on the new-employee form.</summary>
    public async Task<string?> GetSelectedDepartmentTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Department" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    /// <summary>Reads the current value of the Location dropdown's visible text (new-employee form or Employment tab).</summary>
    public async Task<string?> GetSelectedLocationTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Location" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    /// <summary>Reads the current value of the Manager dropdown's visible text on the Employment tab.</summary>
    public async Task<string?> GetSelectedManagerTextAsync()
    {
        var group = page.Locator(".col-md-4, .col-12").Filter(new() { HasText = "Manager" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if a field-level validation message containing <paramref name="messageText"/>
    /// is visible (e.g. "Employee number is required." from EmployeeProfileEditModel's
    /// [Required(ErrorMessage = ...)] attributes) — used to verify a specific required field's
    /// validation, rather than the generic "some error is present" check in <see cref="HasErrorAsync"/>.
    /// </summary>
    public async Task<bool> HasValidationMessageAsync(string messageText) =>
        await page.Locator(".validation-message").Filter(new() { HasText = messageText }).First.IsVisibleAsync();

    /// <summary>
    /// Fills the Employee Number field on the Employment tab. A no-op when the company's numbering
    /// mode is Automatic (the field isn't rendered and a number gets assigned on save instead) —
    /// callers that don't care which mode is active can call this unconditionally rather than
    /// checking <see cref="IsEmployeeNumberInputVisibleAsync"/> themselves first.
    /// </summary>
    public async Task FillEmployeeNumberAsync(string value)
    {
        var field = page.GetByPlaceholder("e.g. EMP-001");

        // A single instant IsVisibleAsync() snapshot can land mid-flicker — _companyEmployeeNumberMode
        // starts as Manual (the field renders) and only flips to Automatic (the field is removed)
        // once EmployeeEdit.razor's own async hrSettings fetch resolves, so a check that fires right
        // as that swap happens can catch neither state reliably. Poll instead of trusting one
        // snapshot — for Manual-mode companies (where the field is required) this avoids silently
        // skipping the fill and failing later with "Employee number is required."; for Automatic-mode
        // companies it just spends a little longer confirming the field really is gone. 10s matches
        // IsEmployeeNumberInputVisibleAsync's own wait for this identical async load — a shorter
        // budget here (previously 2s) could still lose the race under a busy/loaded run and silently
        // skip the fill, which is exactly the failure this poll exists to prevent.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var visible = await field.IsVisibleAsync();
        while (!visible && DateTime.UtcNow < deadline)
        {
            await page.WaitForTimeoutAsync(100);
            visible = await field.IsVisibleAsync();
        }

        if (visible)
        {
            await field.FillAsync(value);
            await page.Keyboard.PressAsync("Tab");
        }
    }

    /// <summary>
    /// Returns true if the Employee Number text input is visible on the new-employee form —
    /// false when the company's numbering mode is Automatic, in which case the informational
    /// message below is shown instead (see <see cref="HasEmployeeNumberAutoAssignedMessageAsync"/>).
    /// Polls briefly rather than taking a single IsVisibleAsync() snapshot — same
    /// _companyEmployeeNumberMode async-load race as that method.
    /// </summary>
    public async Task<bool> IsEmployeeNumberInputVisibleAsync()
    {
        try
        {
            await Assertions.Expect(page.GetByPlaceholder("e.g. EMP-001")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the "An employee number will be assigned automatically when this employee
    /// is created." informational message is visible on the new-employee form (Automatic mode).
    /// Polls briefly rather than taking a single IsVisibleAsync() snapshot — _companyEmployeeNumberMode
    /// is resolved inside EmployeeEdit.razor's own LoadAsync, and GoToNewAsync's wait condition
    /// (span[role='combobox']) can in principle be satisfied by an earlier render pass.
    /// </summary>
    public async Task<bool> HasEmployeeNumberAutoAssignedMessageAsync()
    {
        try
        {
            await Assertions.Expect(page.Locator("p").Filter(new() { HasText = "An employee number will be assigned automatically" }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the "#EMP-001"-style employee number badge shown next to the status badge in the
    /// header of an existing employee's edit page, or null if not present. Polls briefly rather
    /// than taking a single instant snapshot of "span.text-muted" — the header summary is a
    /// separate async-loaded render and GoToAsync's own wait condition (the Details tab's
    /// combobox) can resolve on an earlier render pass before it appears.
    /// </summary>
    public async Task<string?> GetEmployeeNumberHeaderTextAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var spans = await page.Locator("span.text-muted").AllAsync();
            foreach (var span in spans)
            {
                var text = (await span.TextContentAsync())?.Trim();
                if (text is not null && text.StartsWith('#'))
                    return text;
            }
            await page.WaitForTimeoutAsync(200);
        }
        return null;
    }

    // ── Notice period override (Employment tab, "Dates" card) ──────────────────
    //
    // Mirrors the "Override company default notice period" toggle on the Position Profile
    // edit page (see PositionProfileEditPage's own "Notice period override" section) — the
    // same three Syncfusion component types (SfCheckBox/SfDropDownList/SfNumericTextBox),
    // used the same way, just a different checkbox label ("Override notice period" rather
    // than "...company default...") and an additional read-only "Notice source" summary
    // alongside it (see EmployeeEmploymentTab.razor's Dates card).

    /// <summary>
    /// The "row g-3 mt-2" div containing the Unit dropdown and Length numeric field, which
    /// is only present in the DOM while "Override notice period" is checked. Same xpath
    /// sibling-traversal approach as PositionProfileEditPage.NoticePeriodOverrideRow, since
    /// the Unit dropdown here has no adjacent &lt;label&gt; to scope by either.
    /// </summary>
    private ILocator NoticePeriodOverrideRow =>
        page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Override notice period" })
            .Locator("xpath=following-sibling::div[contains(@class,'row')]");

    /// <summary>Checks/unchecks "Override notice period" and waits for the reveal/hide of its fields.</summary>
    public async Task SetOverrideNoticePeriodAsync(bool overrideEnabled)
    {
        var checkbox = page.GetByLabel("Override notice period");
        var isChecked = await checkbox.IsCheckedAsync();
        if (overrideEnabled && !isChecked)
        {
            await checkbox.CheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { Timeout = 10_000 });

            // Comparing against PositionProfileEditPage's equivalent flow (same three component
            // types, same order — checkbox, Unit dropdown, Length numeric — and reliably fast)
            // shows the difference isn't this row's markup: PositionProfileEdit's page has two
            // OTHER SfNumericTextBox fields (Probation Months Override, Salary Range) rendered
            // unconditionally near the top of that form, so by the time its test reaches Notice
            // Period Length, Syncfusion's numeric-textbox JS module has already paid its one-time
            // per-page init cost on an earlier instance. The Employment tab has no such earlier
            // SfNumericTextBox anywhere — this Length field is the first one ever mounted on the
            // page, and (like the first-ever dropdown popup handled in OpenEmploymentTabAsync)
            // that first-of-its-kind cold start is real and can run well past a casual budget.
            // Pay it here, immediately once the field exists, rather than leaving it to
            // TypeIntoNumericInputAsync's later, harder-deadline wait — this gives it the most
            // possible elapsed time (the caller's subsequent Unit dropdown selection included)
            // before anything actually needs it enabled.
            try
            {
                await Assertions.Expect(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First)
                    .ToBeEnabledAsync(new() { Timeout = 90_000 });
            }
            catch (PlaywrightException)
            {
                // Best-effort warm-up only — TypeIntoNumericInputAsync has its own wait and will
                // surface the real failure if it's still not enabled by the time it's needed.
            }
        }
        if (!overrideEnabled && isChecked)
        {
            await checkbox.UncheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }
    }

    public Task<bool> IsOverrideNoticePeriodCheckedAsync() =>
        page.GetByLabel("Override notice period").IsCheckedAsync();

    /// <summary>True once the Unit/Length fields have rendered (i.e. the override checkbox is checked).</summary>
    public Task<bool> IsNoticePeriodOverrideFieldsVisibleAsync() =>
        NoticePeriodOverrideRow.IsVisibleAsync();

    /// <summary>Selects a value ("Weeks" or "Months") from the notice period override's Unit dropdown. Only present once the override checkbox is checked.</summary>
    public Task SelectNoticePeriodUnitAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, NoticePeriodOverrideRow, unitLabel);

    /// <summary>Returns the currently displayed value of the notice period override's Unit dropdown.</summary>
    public async Task<string> GetNoticePeriodUnitTextAsync()
    {
        var combobox = NoticePeriodOverrideRow.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Sets the notice period override's Length numeric field. Only present once the override checkbox is checked.</summary>
    public Task FillNoticePeriodLengthAsync(int length) =>
        TypeIntoNumericInputAsync(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First, length.ToString());

    /// <summary>Returns the current value of the notice period override's Length numeric field.</summary>
    public async Task<int> GetNoticePeriodLengthAsync()
    {
        var value = await NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>
    /// The read-only "Notice source" summary card (a small &lt;dl&gt;) sitting alongside the
    /// override toggle — shows the resolved Source ("Employee"/"Position Profile"/"Company
    /// Default") and Notice Period (length + unit), reflecting EffectiveNoticePeriodResolver's
    /// server-side resolution regardless of whether this employee's own override is set.
    /// </summary>
    private ILocator NoticeSourceSummary =>
        page.Locator(".col-md-6").Filter(new() { HasText = "Notice source" }).First;

    /// <summary>Returns the "Source" value from the Notice source summary (e.g. "Employee", "Position Profile", "Company Default").</summary>
    public async Task<string?> GetNoticeSourceLabelAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>Returns the "Notice Period" value from the Notice source summary (e.g. "3 Weeks").</summary>
    public async Task<string?> GetEffectiveNoticePeriodTextAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").Nth(1);
        return (await dd.TextContentAsync())?.Trim();
    }

    // ── Close / unsaved-changes prompt (EditPageBase) ──────────────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    // RequestClose's button label changed from "Close" to "Cancel" on both the new-employee form
    // and the existing-employee edit mode (see EmployeeEdit.razor) — method name kept as-is since
    // many existing callers already depend on it, only the underlying locator text changed.
    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    // ── Compensation Tab ────────────────────────────────────────────────────────
    // Tab label is "Compensation History" (renamed from "Compensation" — the separate
    // "Current Compensation" panel/card was removed entirely; the tab now shows only the
    // Compensation History card/grid or the single unified empty-state message).

    public async Task OpenCompensationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compensation History" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='compensation-history-grid'], [data-testid='no-compensation-message']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// True if a "Current Compensation" panel/card is rendered on the Compensation History tab —
    /// expected to always be false now that panel was removed entirely; retained only so existing
    /// callers asserting its absence still compile.
    /// </summary>
    public Task<bool> HasCurrentCompensationPanelAsync() =>
        page.Locator("[data-testid='current-compensation-panel']").IsVisibleAsync();

    public async Task<string?> GetCompensationFieldTextAsync(string testId)
    {
        var locator = page.Locator($"[data-testid='{testId}']");
        return await locator.IsVisibleAsync() ? (await locator.TextContentAsync())?.Trim() : null;
    }

    public async Task ClickAddCompensationAsync()
    {
        await page.Locator("[data-testid='add-compensation-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task FillAddCompensationEffectiveFromAsync(string ddMMyyyy)
    {
        var input = page.Locator(".add-compensation-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectAddCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-compensation-dialog"), salaryType);

    // Salary is an SfNumericTextBox with FloatLabelType.Auto, which renders its Placeholder
    // prop as a floating label rather than a native HTML placeholder attribute, so
    // GetByPlaceholder never matches it (see CompanyEditPage.NumericBoxByLabel for the same
    // caveat). Salary is the first e-numerictextbox in the dialog (before Hours Per Week/FTE).
    public Task FillAddCompensationSalaryAsync(string value) =>
        FillNumericAndVerifyAsync(page.Locator(".add-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));

    public async Task FillAddCompensationCurrencyAsync(string value)
    {
        await page.Locator(".add-compensation-dialog").GetByPlaceholder("e.g. GBP").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitAddCompensationDialogAsync()
    {
        await page.Locator(".add-compensation-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // Same "dialog closing doesn't prove the grid's own reload has landed" race as
        // SubmitEditCompensationDialogAsync above — callers that immediately read/act on the
        // history grid (e.g. the newly added row, or a subsequent Delete) can race it.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public Task<bool> HasAddCompensationDialogErrorAsync() =>
        page.Locator(".add-compensation-dialog .alert-danger").IsVisibleAsync();

    public ILocator CompensationHistoryRow(string effectiveFromFragment) =>
        page.Locator("[data-testid='compensation-history-grid'] .e-row").Filter(new() { HasText = effectiveFromFragment });

    public Task ClickEditCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Edit").ClickAsync();

    public Task ClickDeleteCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Delete").ClickAsync();

    public async Task ConfirmDeleteCompensationAsync()
    {
        // Clicking "Yes" triggers an async delete + grid reload round-trip; ClickAsync only
        // waits for the click event to dispatch, not for that round-trip, so callers that
        // immediately check row visibility can race ahead of the reload. Wait for the button
        // itself to disappear (it only renders for the row mid-confirmation) as a signal the
        // grid has actually re-rendered with fresh data.
        var yesButton = page.Locator("[data-testid='compensation-history-grid']").GetByRole(AriaRole.Button, new() { Name = "Yes" });
        await yesButton.ClickAsync();
        await yesButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task FillEditCompensationSalaryAsync(string value)
    {
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        // See FillAddCompensationSalaryAsync — Salary is a FloatLabelType.Auto SfNumericTextBox,
        // so it must be targeted by its e-numerictextbox class rather than GetByPlaceholder.
        await FillNumericAndVerifyAsync(page.Locator(".edit-future-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));
    }

    public async Task SubmitEditCompensationDialogAsync()
    {
        await page.Locator(".edit-future-compensation-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // The dialog closing only proves the save request was accepted, not that the
        // Compensation History grid's own async reload has actually landed yet — a caller that
        // immediately reads the row's text (e.g. to check the edited salary) can race that reload
        // and still see the pre-edit value. Same class of race already fixed on
        // ConfirmDeleteCompensationAsync just below.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    // FillAsync sets a Syncfusion SfNumericTextBox's DOM value through CDP directly, which
    // bypasses the component's own JS keyup/input listeners that sync the typed value back to
    // the Blazor-bound model — so a value that visually "fills" never actually round-trips to
    // the server (see CompanyEditPage.TypeIntoNumericInputAsync for the same issue). Click-to-
    // focus, select-all, delete, then type each character for real.
    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        // Syncfusion renders SfNumericTextBox server-side with the native "disabled" attribute
        // set, and only removes it once its own JS interop has initialized the component client
        // side — the same freshly-mounted-widget race documented at length on DropDownSelector
        // (aria-owns not present until interop finishes) and OpenEmploymentTabAsync, just showing
        // up here as a literal disabled attribute instead of a missing aria attribute. A bare
        // ClickAsync() falls back to Playwright's default 30s actionability wait, which recent
        // evidence under a busy run shows isn't always enough for interop to catch up (see
        // DropDownSelector's own widened click-retry budget) — wait for "enabled" explicitly with
        // the same wider budget rather than relying on the implicit default.
        await input.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Assertions.Expect(input).ToBeEnabledAsync(new() { Timeout = 90_000 });
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        // Give the clear a moment to actually land before typing — observed corruption (e.g.
        // "40000.00420004200042000") is consistent with Ctrl+A/Delete not reliably clearing the
        // field before PressSequentially starts, so each retry of FillNumericAndVerifyAsync's
        // wrapper just appends more text onto the still-present old value instead of replacing it.
        // Same mitigation already applied to the equivalent race in
        // BulkCompensationUpdateDialogPage.SetProposedSalaryAsync.
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Fills a numeric input and confirms the parsed value actually stuck before returning,
    /// retrying if not — a bare "fire and forget" fill can race with Blazor's server round-trip
    /// for the two-way bound value (see CompanyEditPage.FillNumericAndVerifyAsync for the same
    /// issue, originally observed with DefaultHolidayAllowance reverting after save+reload).
    /// </summary>
    private async Task FillNumericAndVerifyAsync(ILocator input, string value, decimal expected, int maxAttempts = 3)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await TypeIntoNumericInputAsync(input, value);

            var actual = await input.InputValueAsync();
            if (decimal.TryParse(actual, out var parsed) && parsed == expected)
                return;

            if (attempt < maxAttempts)
                await page.WaitForTimeoutAsync(200);
        }

        throw new PlaywrightException(
            $"Numeric input value did not stick after {maxAttempts} attempts: expected '{expected}', got '{await input.InputValueAsync()}'.");
    }

    // ── Promotion History Tab ───────────────────────────────────────────────────

    public async Task OpenPromotionHistoryTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Promotion History" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='promote-employee-btn']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the "No promotions recorded for this employee." empty state (HrEmptyState)
    /// is visible — i.e. the employee has no promotion history yet.
    /// </summary>
    public Task<bool> HasNoPromotionsMessageAsync() =>
        page.Locator(".hr-empty-state").Filter(new() { HasText = "No promotions recorded for this employee." }).IsVisibleAsync();

    /// <summary>Returns true if the promotion history grid is currently rendered (i.e. at least one promotion exists).</summary>
    public Task<bool> HasPromotionHistoryGridAsync() =>
        page.Locator(".e-grid").IsVisibleAsync();

    /// <summary>
    /// Returns the promotion history grid row whose rendered text contains
    /// <paramref name="textFragment"/> (e.g. a Reason or a position title).
    /// </summary>
    public ILocator PromotionHistoryRow(string textFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = textFragment });

    // ── Audit Tab ───────────────────────────────────────────────────────────────

    public async Task OpenAuditTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Audit" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='audit-history-grid'], .alert-secondary",
            new() { Timeout = 15_000 });

        // The grid container above mounts before Syncfusion populates its ".e-row" data on a
        // separate JS tick (same "container before content" race fixed elsewhere in this suite) —
        // a caller that immediately checks AuditHistoryRow(...).IsVisibleAsync() can otherwise see
        // no rows for an employee who genuinely has audit history. Only applies when the grid
        // itself rendered (not the ".alert-secondary" empty state, which never has any rows).
        if (await page.Locator("[data-testid='audit-history-grid']").IsVisibleAsync())
        {
            await page.WaitForSelectorAsync(
                "[data-testid='audit-history-grid'] .e-row, [data-testid='audit-history-grid'] .e-emptyrow",
                new() { Timeout = 15_000 });
        }
    }

    public ILocator AuditHistoryRow(string actionFragment) =>
        page.Locator("[data-testid='audit-history-grid'] .e-row").Filter(new() { HasText = actionFragment });

    public async Task ClickViewAuditRowAsync(string actionFragment)
    {
        await AuditHistoryRow(actionFragment).First.GetByText("View").ClickAsync();
        // Ensure the resulting dialog has actually opened before returning, rather than leaving
        // callers that immediately check HasAuditDetailDialogAsync() to race the open animation
        // with a bare instant check.
        await page.Locator("[role='dialog'].audit-history-detail-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task<bool> HasAuditDetailDialogAsync() =>
        await page.Locator("[role='dialog'].audit-history-detail-dialog").IsVisibleAsync();

    public async Task<string?> GetAuditDetailDialogTextAsync() =>
        await page.Locator("[role='dialog'].audit-history-detail-dialog").TextContentAsync();

    public async Task CloseAuditDetailDialogAsync()
    {
        await page.Locator(".audit-history-detail-dialog .e-footer-content button:has-text('Close')").ClickAsync();
        await page.Locator("[role='dialog'].audit-history-detail-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Probation Tab ──────────────────────────────────────────────────────────

    public async Task OpenProbationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).ClickAsync();
        // EmployeeEdit.razor always renders a ".card" above the tab strip (e.g. the "Reporting
        // Chain" card, for any employee who has a manager) — waiting on a bare
        // ".card, .alert-secondary" selector resolves immediately against that pre-existing card
        // instead of EmployeeProbationTab's own async-loaded content, so callers that immediately
        // read the status badge can catch it while the tab's own spinner is still showing. Wait
        // for the spinner to clear first, then for the tab's own content specifically (the
        // period-summary progress bar, or the "no record" empty state).
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(".progress, .alert-secondary", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the probation period summary panel (progress bar card) is visible.</summary>
    public async Task<bool> HasProbationPeriodSummaryPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Syncfusion review history grid is visible on the Probation tab.</summary>
    public async Task<bool> HasProbationReviewsGridAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    /// <summary>Returns the text of the probation status badge on the Probation tab summary panel.</summary>
    public async Task<string?> GetProbationStatusBadgeTextAsync()
    {
        // ".card .badge" alone also matches the "Current Employee" badge in EmployeeEdit.razor's
        // Reporting Chain card, which sits above the tab strip and comes first in DOM order — for
        // any employee who has a manager, .First landed on that badge instead of the Probation
        // Record card's own status badge. Scope to the card whose header says "Probation Record"
        // specifically (see EmployeeProbationTab.razor).
        var badge = page.Locator(".card").Filter(new() { Has = page.Locator(".card-header:has-text('Probation Record')") })
            .Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the status badge text for the first review row in the review history grid
    /// whose ReviewType cell contains <paramref name="reviewTypeFragment"/>.
    /// </summary>
    public async Task<string?> GetReviewStatusInGridAsync(string reviewTypeFragment)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var rows = await page.Locator(".e-grid .e-row").AllAsync();
        foreach (var row in rows)
        {
            var text = await row.TextContentAsync();
            if (text?.Contains(reviewTypeFragment, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            // Status badge is within the row — grab the first badge element.
            var badge = row.Locator(".badge").First;
            if (await badge.IsVisibleAsync())
                return (await badge.TextContentAsync())?.Trim();
        }

        return null;
    }

    // ── Tasks Tab ───────────────────────────────────────────────────────────────

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, .task-cell, p", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the task row (by task id) in the admin Tasks tab grid, opening TaskViewDialog.
    /// Use TaskViewPage.WaitForLoadedAsync (or its own methods) to read the opened task's content.
    /// </summary>
    public async Task ClickTaskAsync(Guid taskId)
    {
        var row = page.Locator($"[data-testid='task-view-btn-{taskId}']");
        await row.WaitForAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }

    // ── Sickness Tab ────────────────────────────────────────────────────────────

    public async Task OpenSicknessTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Sickness" }).ClickAsync();
        // Same trap as OpenProbationTabAsync: EmployeeEdit.razor always renders a ".card" above
        // the tab strip, so a bare ".card, .alert-secondary" wait resolves immediately against
        // that pre-existing card instead of EmployeeSicknessTab's own async-loaded content — the
        // ensuing HasSicknessGridAsync check then races Syncfusion's own JS render pass for the
        // grid. Wait for the spinner to clear first, then for the grid itself.
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 15_000 });
    }

    public Task<bool> HasSicknessGridAsync() =>
        page.Locator(".e-grid").First.WaitUntilVisibleAsync();

    public async Task OpenRecordSicknessDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Record Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    /// <summary>Selects a category in the (currently open) Record Sickness dialog.</summary>
    public Task SelectRecordSicknessCategoryAsync(string categoryName) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator("[role='dialog'].record-sickness-dialog .col-12").Filter(new() { HasText = "Category" }).First,
            categoryName);

    /// <summary>Fills the Start Date field in the (currently open) Record Sickness dialog.</summary>
    public async Task FillRecordSicknessStartDateAsync(string ddMMyyyy)
    {
        var input = page.Locator("[role='dialog'].record-sickness-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitRecordSicknessAsync()
    {
        await page.Locator("[role='dialog'].record-sickness-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Record", Exact = true })
            .ClickAsync();
        await page.Locator("[role='dialog'].record-sickness-dialog")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<bool> HasRecordSicknessErrorAsync() =>
        await page.Locator("[role='dialog'].record-sickness-dialog .alert-danger").IsVisibleAsync();

    /// <summary>
    /// Returns the status badge text for the sickness grid row whose Start Date column
    /// contains <paramref name="startDateddMMMyyyy"/> (e.g. "05 Jul 2026", matching the
    /// grid's "dd MMM yyyy" display format).
    /// </summary>
    public async Task<string?> GetSicknessStatusBadgeForStartDateAsync(string startDateddMMMyyyy)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;

        var badge = row.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Clicks the "Close" action button on the grid row matching the given start date.</summary>
    public async Task StartCloseSicknessRecordAsync(string startDateddMMMyyyy)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;
        // Button text is "Close" (the "Close record" title attribute is only a tooltip —
        // accessible name computation prefers the visible text content).
        await row.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].close-sickness-record-dialog", new() { Timeout = 10_000 });
    }

    /// <summary>Fills the End Date field in the (currently open) Close Sickness Record dialog.</summary>
    public async Task FillCloseSicknessEndDateAsync(string ddMMyyyy)
    {
        var input = page.Locator("[role='dialog'].close-sickness-record-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitCloseSicknessRecordAsync()
    {
        await page.Locator("[role='dialog'].close-sickness-record-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Close Record" })
            .ClickAsync();
        await page.Locator("[role='dialog'].close-sickness-record-dialog")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Onboarding Tab ──────────────────────────────────────────────────────────

    public async Task OpenOnboardingTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).ClickAsync();
        // Wait for the tab content to render — either the progress panel's progress bar
        // (a plan exists) or the "No onboarding plan found for this employee" empty state.
        await page.WaitForSelectorAsync(".progress, .hr-empty-state", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the onboarding progress panel (status badge + progress bar) is visible.</summary>
    public async Task<bool> HasOnboardingProgressPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Onboarding Checklist card is visible.</summary>
    public async Task<bool> HasOnboardingChecklistAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Checklist')").IsVisibleAsync();

    /// <summary>Returns true if the Onboarding Timeline card is visible.</summary>
    public async Task<bool> HasOnboardingTimelineAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Timeline')").IsVisibleAsync();

    /// <summary>Returns the text of the onboarding plan status badge on the progress panel.</summary>
    public async Task<string?> GetOnboardingStatusBadgeTextAsync()
    {
        // Scoped to the card containing the progress bar, not just any ".card .badge" — the
        // Reporting Chain card (rendered above the tabs whenever the employee has a manager)
        // also has a badge ("Current Employee"), and an unscoped .First would grab that instead.
        var badge = page.Locator(".card:has(.progress) .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the current onboarding progress percentage, read from the progress bar's
    /// aria-valuenow attribute (more robust than scraping the "NN%" caption text).
    /// </summary>
    public async Task<int> GetOnboardingProgressPercentAsync()
    {
        var bar = page.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    /// <summary>
    /// Returns the status badge text ("Pending"/"In Progress"/"Completed"/"Overdue"/"Skipped")
    /// for the Onboarding Checklist row whose Task cell contains <paramref name="taskTitleFragment"/>.
    /// Scoped to the "Onboarding Checklist" card specifically, since the Outstanding Document
    /// Requests / Outstanding Asset Acknowledgements cards below it share the same table classes.
    /// </summary>
    public async Task<string?> GetOnboardingChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Onboarding Checklist" }).First;
        var row = checklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    // ── Profile Photo Header (EmployeeProfilePhotoHeader — HR-managed photo for another employee) ──
    // Rendered near the top of the page (outside the tabs) whenever Session.CanManageEmployees
    // is true; see EmployeeEdit.razor.

    private ILocator ProfilePhotoImage => page.Locator("img.hr-profile-avatar");
    private ILocator ProfilePhotoInitials => page.Locator("span.hr-profile-avatar--initials");

    private ILocator PendingProfilePhotoCard =>
        page.Locator(".alert-info").Filter(new() { HasText = "Pending Review" }).First;

    /// <summary>
    /// Returns true if the profile photo header is showing an actual photo (an &lt;img&gt;)
    /// rather than the initials placeholder — i.e. the employee has an approved current photo.
    /// Polls briefly (like <see cref="HasProfilePhotoInitialsAsync"/>) rather than taking a single
    /// snapshot — see that method's own comment for why.
    /// </summary>
    public async Task<bool> HasProfilePhotoImageAsync()
    {
        try
        {
            await Assertions.Expect(ProfilePhotoImage).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the profile photo header is showing the initials placeholder — i.e. the
    /// employee has no approved current photo yet (see ProfilePhotoAvatar's fallback rendering).
    /// Polls briefly rather than taking a single IsVisibleAsync() snapshot — EmployeeProfilePhotoHeader
    /// loads its current-photo state asynchronously and can render after GoToAsync's own wait
    /// condition (the Details tab's combobox) has already resolved on an earlier render pass, same
    /// race class as the Probation/Notes tabs.
    /// </summary>
    public async Task<bool> HasProfilePhotoInitialsAsync()
    {
        try
        {
            await Assertions.Expect(ProfilePhotoInitials).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// HR uploads a photo directly via the "Upload / Replace Photo" button on the Employee Edit
    /// page header. Unlike self-service uploads, this writes straight to the approved/current
    /// photo table — no pending-review step. Per EmployeeProfilePhotoHeader.HandleUploaded, the
    /// dialog only closes (an EventCallback-driven re-render of the parent) after the header has
    /// already reloaded the current photo, so callers can assert on the new state immediately
    /// after this returns.
    /// </summary>
    public async Task UploadProfilePhotoDirectAsync(string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload / Replace Photo" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload / Replace Profile Photo" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if a pending profile photo review card ("Pending Review") is visible in the
    /// header. Polls briefly rather than taking a single IsVisibleAsync() snapshot — same
    /// EmployeeProfilePhotoHeader async-load race as HasProfilePhotoInitialsAsync/
    /// HasProfilePhotoImageAsync, and this is typically checked right after a self-service upload
    /// navigates HR here, before the header's own pending-photo fetch has necessarily finished.
    /// </summary>
    public async Task<bool> HasPendingProfilePhotoCardAsync()
    {
        try
        {
            await Assertions.Expect(PendingProfilePhotoCard).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Approves the pending profile photo shown in the header's review card. Waits for the card
    /// to disappear as the signal that the header has refreshed with the newly-approved photo
    /// (ApproveAsync reloads both the pending and current photo before its final render).
    /// </summary>
    public async Task ApprovePendingProfilePhotoAsync()
    {
        var card = PendingProfilePhotoCard;
        await card.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await card.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Notes Tab ───────────────────────────────────────────────────────────────
    // Only rendered when Session.IsHrAdministrator (see EmployeeEdit.razor, wrapped in an
    // @if(Session.IsHrAdministrator) around the "Notes" TabHeader/TabContent, in addition to the
    // page-level Session.CanManageEmployees guard applied to the whole edit page).

    public async Task OpenNotesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Notes" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='add-note-btn'], [data-testid='no-notes-message']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the "Notes" tab is visible — polls briefly rather than taking a single
    /// IsVisibleAsync() snapshot, since (like Probation) it only renders once EmployeeEdit.razor's
    /// own async LoadAsync sets its HR-administrator-gated flag, which can land after GoToAsync's
    /// own wait condition (the Details tab's combobox) has already resolved on an earlier render.
    /// </summary>
    public async Task<bool> HasNotesTabAsync()
    {
        try
        {
            await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Notes" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task ClickAddNoteAsync()
    {
        var dialog = page.Locator("[role='dialog'].add-employee-note-dialog");
        var addNoteBtn = page.Locator("[data-testid='add-note-btn']");

        // A click landing while the previous dialog's close is still committing server-side
        // (see SubmitAddNoteDialogAsync's own comment) can be a no-op against still-IsOpen=true
        // state — a fixed debounce there reduces but doesn't eliminate the race, especially across
        // many iterations in a row (e.g. a loop adding several notes), so retry the click here too
        // rather than trusting a single attempt.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await addNoteBtn.ClickAsync();
            try
            {
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                await WaitForCategoryComboboxAsync();
                return;
            }
            catch (TimeoutException)
            {
                // fall through and retry the click
            }
        }

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await WaitForCategoryComboboxAsync();

        // The dialog root becoming visible only proves Syncfusion's SfDialog shell has opened —
        // its own body content, including the Category SfDropDownList that
        // SelectAddNoteCategoryAsync immediately targets next, is a separate, later render pass.
        // Calling DropDownSelector against a combobox that hasn't mounted yet fails hard: Playwright's
        // default (30s) actionability wait just spins waiting for an element that was never there
        // to begin with, rather than timing out quickly the way an already-attached-but-not-yet-
        // interactive element would. Wait for it explicitly here so callers never race this gap.
        async Task WaitForCategoryComboboxAsync() =>
            await dialog.Locator("span[role='combobox']").First.WaitForAsync(
                new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
    }

    /// <summary>
    /// Selects a category from the Add Note dialog's Category dropdown. DropDownSelector itself
    /// confirms Blazor's ValueChanged round-trip actually committed the selection before
    /// returning — see its own doc comment.
    /// </summary>
    public Task SelectAddNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-employee-note-dialog"), categoryLabel);

    // Targets the placeholder text rather than [data-testid='add-note-text'] — that attribute is
    // passed via HrTextBox's HtmlAttributes, which for a Multiline SfTextBox can land on the outer
    // ".e-input-group" wrapper rather than the actual <textarea> (see AddEmployeeNoteDialog.razor),
    // so filling by that selector can silently write into an element that isn't bound to
    // Model.NoteText at all. GetByPlaceholder targets the real input directly, matching the
    // pattern already used successfully elsewhere (e.g. PromoteEmployeeDialog.FillReasonAsync).
    public async Task FillAddNoteTextAsync(string text)
    {
        await page.GetByPlaceholder("Enter note details…").FillAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task CheckAddNoteImportantAsync() =>
        page.Locator(".add-employee-note-dialog").GetByLabel("Important").CheckAsync();

    public async Task SubmitAddNoteDialogAsync()
    {
        var dialog = page.Locator("[role='dialog'].add-employee-note-dialog");
        var addBtn = page.Locator(".add-employee-note-dialog .e-footer-content button:has-text('Add')");

        // Same race as ClickAddNoteAsync's own retry loop (see its comment), just on the submit
        // side instead of the reopen side: a click landing while the dialog's own open/prior-close
        // commit is still in flight server-side can be silently swallowed with no error and no
        // client-side signal — observed as the dialog just never transitioning to Hidden, not as a
        // late-but-eventual close. That's indistinguishable from a genuinely failed submit by
        // waiting alone, so retry the click itself rather than only waiting longer for one attempt.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await addBtn.ClickAsync();
            try
            {
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = attempt < 3 ? 4_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                // Click likely landed mid-commit and was a no-op — try again.
            }
        }

        // The dialog goes visually Hidden (Syncfusion toggles the e-popup-close class client-side)
        // ahead of the SignalR round-trip that actually commits IsOpen=false server-side — a caller
        // that immediately reopens the dialog (e.g. a loop adding several notes in a row) can click
        // "Add Note" before that commit lands, and the reopen is a no-op against still-IsOpen=true
        // server state. Same race class as DropDownSelector's own popup-close debounce.
        await page.WaitForTimeoutAsync(250);
    }

    public Task<bool> HasAddNoteDialogErrorAsync() =>
        page.Locator(".add-employee-note-dialog .alert-danger").IsVisibleAsync();

    /// <summary>
    /// True if the Notes tab's grid rendered a pager at all — Syncfusion's SfGrid doesn't render
    /// ".e-pagercontainer" when every row already fits on one page. Same convention as
    /// EmployeeDirectoryReportPage.IsPagerVisibleAsync.
    /// </summary>
    public Task<bool> IsNotesGridPagerVisibleAsync() =>
        page.Locator("[data-testid='employee-notes-grid'] .e-pagercontainer").IsVisibleAsync();

    /// <summary>Returns the notes grid row whose rendered text contains <paramref name="textFragment"/>.</summary>
    public ILocator NoteCard(string textFragment) =>
        page.Locator("[data-testid='employee-notes-grid'] .e-row").Filter(new() { HasText = textFragment });

    public async Task ClickSupersedeNoteAsync(string originalTextFragment)
    {
        await NoteCard(originalTextFragment).First
            .Locator("[data-testid='edit-note-btn']")
            .ClickAsync();
        await page.Locator("[role='dialog'].supersede-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task FillSupersedeNoteTextAsync(string text)
    {
        await page.Locator("[data-testid='supersede-note-text']").FillAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectSupersedeNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".supersede-employee-note-dialog"), categoryLabel);

    public async Task SubmitSupersedeNoteDialogAsync()
    {
        await page.Locator(".supersede-employee-note-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].supersede-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>True if the note card containing <paramref name="textFragment"/> shows the "Superseded" badge.</summary>
    public Task<bool> NoteCardHasSupersededBadgeAsync(string textFragment) =>
        NoteCard(textFragment).First.Locator("[data-testid='note-superseded-badge']").IsVisibleAsync();

    /// <summary>True if the note card containing <paramref name="textFragment"/> shows the "Important" badge.</summary>
    public Task<bool> NoteCardHasImportantBadgeAsync(string textFragment) =>
        NoteCard(textFragment).First.Locator("[data-testid='note-important-badge']").IsVisibleAsync();

    /// <summary>Returns the bounding-box Y position of the note card containing <paramref name="textFragment"/> — used to assert relative ordering (important notes pinned above standard notes).</summary>
    public async Task<float?> GetNoteCardYPositionAsync(string textFragment)
    {
        var box = await NoteCard(textFragment).First.BoundingBoxAsync();
        return box?.Y;
    }
}
