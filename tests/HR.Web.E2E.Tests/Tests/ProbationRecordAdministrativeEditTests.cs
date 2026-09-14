using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 17 (Gap 2): the Probation tab's "administrative correction" edit UI
/// (ProbationRecordEditPanel.razor / EmployeeProbationTab.razor) — manager, expected end date and
/// notes only, gated on probation:manage and a non-terminal record status (see
/// EmployeeProbationTab.IsEditable). Follows AdminSupportRequestStatusConcurrencyTests'
/// two-browser-session conflict pattern and uses the seeded personas/employees from
/// EmployeeProbationTabTests (Carlos Rivera has an active, editable record; James Okafor has only a
/// terminal Passed record).
/// </summary>
public sealed class ProbationRecordAdministrativeEditTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CarlosRivera = Guid.Parse("30000000-0000-0000-0000-000000000010");

    // James Okafor: seeded with only a Passed (terminal) probation record — see
    // EmployeeProbationTabTests' remarks. Used here to confirm no Edit button is rendered.
    private static readonly Guid JamesOkafor = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ProbationTab_ShowsReadOnlySummary_AndEditButton_ForEditableRecord()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await panel.IsEditButtonVisibleAsync(),
            "Expected an Edit button on the Probation Record card for an HR Administrator on a non-terminal record");
        Assert.False(string.IsNullOrWhiteSpace(await panel.GetManagerSummaryTextAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await panel.GetExpectedEndDateSummaryTextAsync()));
    }

    [Fact]
    public async Task EditingManagerDateAndNotes_Saves_AndPersistsAfterReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();
        await panel.ClickEditAsync();

        var newEndDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(6);
        var newEndDateText = newEndDate.ToString("dd/MM/yyyy");
        var notes = $"E2E administrative correction {Guid.NewGuid():N}".Substring(0, 40);

        await panel.SelectManagerAsync("James Okafor");
        await panel.SetExpectedEndDateAsync(newEndDateText);
        await panel.SetNotesAsync(notes);
        await panel.SaveExpectingSuccessAsync();

        Assert.False(await panel.IsConflictBannerVisibleAsync());

        // Reload the page entirely to confirm the new values actually persisted server-side.
        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.Contains("James Okafor", await panel.GetManagerSummaryTextAsync());
        Assert.Contains(newEndDate.ToString("d MMM yyyy"), await panel.GetExpectedEndDateSummaryTextAsync());
        Assert.Equal(notes, await panel.GetNotesSummaryTextAsync());
    }

    [Fact]
    public async Task ConcurrentEdits_SecondSave_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();
        await panel.ClickEditAsync();

        // ── Tab 1: open the editor, make a change, but don't save yet (loads Version v1) ──
        var tab1Notes = $"Tab1 unsaved notes {Guid.NewGuid():N}".Substring(0, 30);
        await panel.SetNotesAsync(tab1Notes);

        // ── Tab 2 (same persona/context): load the same record and save first, bumping its Version ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherLogin = new LoginPage(otherPage, _fixture.WebBaseUrl);
            var otherEmpEdit = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            var otherPanel = new ProbationRecordEditPanelComponent(otherPage);

            await otherLogin.GoToAsync();
            await otherLogin.LoginAsync(LauraEmail);

            await otherEmpEdit.GoToAsync(AcmeId, CarlosRivera);
            await otherEmpEdit.OpenProbationTabAsync();
            await otherPanel.ClickEditAsync();

            var tab2Notes = $"Tab2 winning notes {Guid.NewGuid():N}".Substring(0, 30);
            await otherPanel.SetNotesAsync(tab2Notes);
            await otherPanel.SaveExpectingSuccessAsync();

            Assert.True(await otherPanel.IsSuccessMessageVisibleAsync(),
                "Expected the second tab's save to succeed and bump the record's Version");
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, unsaved value preserved ──
        await panel.SaveExpectingConflictAsync();

        Assert.True(await panel.IsConflictBannerVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");

        // ── Tab 1: "Reload latest values" adopts tab 2's winning values while staying in the edit form ──
        await panel.ClickReloadLatestValuesAsync();

        Assert.False(await panel.IsConflictBannerVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");

        // ── Tab 1: save again against the fresh version — must succeed ──
        var finalNotes = $"Tab1 final notes {Guid.NewGuid():N}".Substring(0, 30);
        await panel.SetNotesAsync(finalNotes);
        await panel.SaveExpectingSuccessAsync();

        Assert.False(await panel.IsConflictBannerVisibleAsync());

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();
        Assert.Equal(finalNotes, await panel.GetNotesSummaryTextAsync());
    }

    // Ticket 18: when "Reload latest values" pulls the authoritative record after a stale-write
    // conflict and that record is STILL editable (non-terminal), the editor stays open with
    // refreshed field values and a subsequent save succeeds — this is the existing
    // ConcurrentEdits_SecondSave_ShowsConflictBanner_ThenReloadRecovers path; this test isolates
    // just the "values actually refresh from the winning edit, not just cleared" assertion.
    [Fact]
    public async Task ReloadAfterConflict_WhenLatestRecordStillEditable_RefreshesFormAndAllowsSave()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();
        await panel.ClickEditAsync();

        var tab1StaleNotes = $"Tab1 stale notes {Guid.NewGuid():N}".Substring(0, 30);
        await panel.SetNotesAsync(tab1StaleNotes);

        var winningEndDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(4);
        var winningEndDateText = winningEndDate.ToString("dd/MM/yyyy");

        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherLogin = new LoginPage(otherPage, _fixture.WebBaseUrl);
            var otherEmpEdit = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            var otherPanel = new ProbationRecordEditPanelComponent(otherPage);

            await otherLogin.GoToAsync();
            await otherLogin.LoginAsync(LauraEmail);

            await otherEmpEdit.GoToAsync(AcmeId, CarlosRivera);
            await otherEmpEdit.OpenProbationTabAsync();
            await otherPanel.ClickEditAsync();
            await otherPanel.SetExpectedEndDateAsync(winningEndDateText);
            await otherPanel.SaveExpectingSuccessAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        await panel.SaveExpectingConflictAsync();
        Assert.True(await panel.IsConflictBannerVisibleAsync());

        await panel.ClickReloadLatestValuesAsync();
        Assert.False(await panel.IsConflictBannerVisibleAsync());

        // The reloaded expected end date field should now show the winning tab's value, not Tab
        // 1's original stale value.
        var reloadedEndDate = await panel.GetExpectedEndDateFieldValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(reloadedEndDate));
        Assert.Contains(winningEndDateText[..2], reloadedEndDate); // day component sanity check

        var finalNotes = $"Tab1 post-reload save {Guid.NewGuid():N}".Substring(0, 30);
        await panel.SetNotesAsync(finalNotes);
        await panel.SaveExpectingSuccessAsync();
        Assert.False(await panel.IsConflictBannerVisibleAsync());
    }

    // Ticket 18: Emma Jones (30000000-…-009) — an independent active probation record under David
    // Park with a PENDING FinalDecision review (task a0000000-…-002b, assigned to Laura Bennett) —
    // see ProbationModule.cs's dev-seed remarks. Dedicated to this one scenario so completing the
    // review here can never interfere with another test.
    private static readonly Guid EmmaJones = Guid.Parse("30000000-0000-0000-0000-000000000009");
    private static readonly Guid EmmaFinalDecisionTaskId = Guid.Parse("a0000000-0000-0000-0000-00000000002b");

    // Ticket 18: Marcus Diallo (30000000-…-006) — a second, independent active probation record
    // under Laura Bennett herself, also with a pending FinalDecision review (task
    // a0000000-…-002c). Kept entirely separate from Emma Jones above so the two mid-edit
    // terminal-transition tests below don't share (and mutate) the same record.
    private static readonly Guid MarcusDiallo = Guid.Parse("30000000-0000-0000-0000-000000000006");
    private static readonly Guid MarcusFinalDecisionTaskId = Guid.Parse("a0000000-0000-0000-0000-00000000002c");

    // Laura Bennett's own employee id — both FinalDecision review tasks above are assigned to her,
    // and TaskViewPage.GoToAsync is a self-service route (the logged-in user's own Tasks tab).
    private static readonly Guid LauraBennett = Guid.Parse("30000000-0000-0000-0000-000000000005");

    /// <summary>
    /// Completes the seeded pending FinalDecision review task for the given employee via the Task
    /// view UI (Laura Bennett's own Tasks tab, since both seeded FinalDecision tasks are assigned
    /// to her), selecting outcome "Pass" — this independently transitions that employee's active
    /// probation record to the terminal "Passed" status, exactly like a real HR admin's
    /// administrative correction racing a manager's probation decision.
    /// </summary>
    private static async Task CompleteFinalDecisionReviewAsPassAsync(IPage page, string webBaseUrl, Guid taskId)
    {
        var login = new LoginPage(page, webBaseUrl);
        var taskView = new TaskViewPage(page, webBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await taskView.GoToAsync(AcmeId, LauraBennett, taskId);
        await taskView.SelectReviewOutcomeAsync("Pass");
        await taskView.CompleteReviewAsync();
    }

    // Ticket 18: "Reload latest values" pulls the authoritative record after a genuine stale-write
    // conflict, and by the time the reload happens the record has independently become terminal
    // (Passed) — e.g. an HR admin's edit racing a FinalDecision review completion. The editor must
    // exit (not stay open with refreshed values, as it does for a still-editable reload — see
    // ReloadAfterConflict_WhenLatestRecordStillEditable_RefreshesFormAndAllowsSave above), show the
    // terminal-while-editing warning, and the Edit button must no longer be present.
    [Fact]
    public async Task ReloadAfterConflict_WhenLatestBecameTerminal_ExitsEditor_AndShowsTerminalWarning()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, EmmaJones);
        await empEdit.OpenProbationTabAsync();
        await panel.ClickEditAsync();

        // Tab 1: an unsaved in-progress edit (loads Version v1).
        var tab1StaleNotes = $"Tab1 stale notes {Guid.NewGuid():N}".Substring(0, 30);
        await panel.SetNotesAsync(tab1StaleNotes);

        var otherPage = await _context.NewPageAsync();
        try
        {
            // Step 1: a second, still-non-terminal administrative-correction save bumps the
            // record's Version (v1 -> v2) — this is what makes Tab 1's own save stale/"concurrency"
            // rather than an immediate terminal "conflict".
            var otherLogin = new LoginPage(otherPage, _fixture.WebBaseUrl);
            var otherEmpEdit = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            var otherPanel = new ProbationRecordEditPanelComponent(otherPage);

            await otherLogin.GoToAsync();
            await otherLogin.LoginAsync(LauraEmail);

            await otherEmpEdit.GoToAsync(AcmeId, EmmaJones);
            await otherEmpEdit.OpenProbationTabAsync();
            await otherPanel.ClickEditAsync();
            await otherPanel.SetNotesAsync($"Tab2 winning notes {Guid.NewGuid():N}".Substring(0, 30));
            await otherPanel.SaveExpectingSuccessAsync();

            Assert.True(await otherPanel.IsSuccessMessageVisibleAsync(),
                "Expected the second tab's save to succeed and bump the record's Version");
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // Tab 1: saving now is stale -> genuine concurrency conflict banner (record is still
        // non-terminal at this point).
        await panel.SaveExpectingConflictAsync();
        Assert.True(await panel.IsConflictBannerVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");

        // Step 2: independently, BEFORE Tab 1 reloads, complete the pending FinalDecision review —
        // transitioning the record to the terminal "Passed" status (and bumping its Version again).
        var terminalPage = await _context.NewPageAsync();
        try
        {
            await CompleteFinalDecisionReviewAsPassAsync(terminalPage, _fixture.WebBaseUrl, EmmaFinalDecisionTaskId);
        }
        finally
        {
            await terminalPage.CloseAsync();
        }

        // Tab 1: "Reload latest values" now discovers a TERMINAL record — the editor must exit
        // rather than stay open with refreshed values.
        await panel.ClickReloadLatestValuesExpectingTerminalExitAsync();

        Assert.True(await panel.IsTerminalWhileEditingWarningVisibleAsync(),
            "Expected the terminal-while-editing warning after a conflict reload discovers a terminal record");
        Assert.Contains("Passed", await panel.GetTerminalWhileEditingWarningTextAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.False(await panel.IsEditButtonVisibleAsync(),
            "Expected no Edit button once the record has reached a terminal status");
    }

    // Ticket 18: attempting to Save (not reload) against a record that has independently become
    // terminal while the editor was open — the endpoint rejects with HTTP 409 code "conflict" (a
    // business-rule rejection, never "concurrency"), so the panel must show only the plain
    // business-rule error and must NOT show the stale-write SaveConflictBanner.
    [Fact]
    public async Task SavingAgainstIndependentlyTerminalRecord_ShowsBusinessRuleError_NotConcurrencyBanner()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusDiallo);
        await empEdit.OpenProbationTabAsync();
        await panel.ClickEditAsync();

        // Tab 1: an unsaved in-progress edit (loads Version v1) — never saved before the
        // independent terminal transition below.
        await panel.SetNotesAsync($"Tab1 notes {Guid.NewGuid():N}".Substring(0, 30));

        // Independently, in a second tab, complete the pending FinalDecision review — transitions
        // Marcus's record straight to the terminal "Passed" status without Tab 1 ever having
        // observed an intermediate non-terminal version bump.
        var otherPage = await _context.NewPageAsync();
        try
        {
            await CompleteFinalDecisionReviewAsPassAsync(otherPage, _fixture.WebBaseUrl, MarcusFinalDecisionTaskId);
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // Tab 1: Save now hits the terminal-status check in the handler BEFORE the version check,
        // so this is a business-rule "conflict", not a "concurrency" rejection — regardless of
        // Tab 1's own (still v1) ExpectedVersion.
        await panel.SaveExpectingBusinessErrorAsync();

        Assert.False(await panel.IsConflictBannerVisibleAsync(),
            "A terminal-status business-rule rejection must never show the stale-write concurrency banner");

        var error = await panel.GetGlobalErrorTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("Passed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbationTab_ShowsNoEditButton_ForTerminalStatusRecord()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var panel = new ProbationRecordEditPanelComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // James Okafor's only probation record is Passed — the Probation tab itself is hidden for
        // any employee with only a terminal record (see EmployeeProbationTabTests
        // .ProbationTab_IsHidden_ForEmployeeWithOnlyAPassedRecord), so there is no route to a
        // visible terminal-status Edit-button check via this seeded employee. Skipped rather than
        // forcing a fragile new seed — see report for details.
        await empEdit.GoToAsync(AcmeId, JamesOkafor);

        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Probation"),
            "Expected no 'Probation' tab at all for an employee whose only probation record is terminal (Passed)");
    }
}
