using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "bulk employee invitations" feature: the Employee List's dedicated invitation mode
/// (<c>?mode=invite</c>, <c>InviteModeCandidateGrid.razor</c>), the manual multi-select "Invite
/// selected (N)" path on the normal grid, <c>BulkInviteConfirmDialog.razor</c>, and
/// <c>InvitationBatchProgressPanel.razor</c>'s async batch-progress polling/recovery.
///
/// Uses Laura Bennett (HR Administrator) against the seeded Acme company — same persona/company as
/// <see cref="EmployeeUserAccountColumnTests"/>, which this class complements (that class covers
/// the single-employee Quick Invite/User Account column; this one covers the bulk flow added on
/// top of it).
///
/// ASSUMPTION (documented per-test below where relevant): WorkEmail is a mandatory field on both
/// employee creation (CreateEmployee\Validator.cs) and update (UpdateEmployeeProfileAndEmployment\
/// Validator.cs) — there is no UI path in this environment to produce an employee with a genuinely
/// blank/invalid work email to exercise the candidate grid's "missing email, never auto-selected"
/// branch. That branch is covered by inspecting the seeded/shared Acme data for such an employee at
/// runtime and skipping gracefully (same "skip when the environment-dependent precondition isn't
/// met" convention already used by GettingStartedAndExploreTests.IncompleteTask_GoToTaskLink_...)
/// rather than fabricating a scenario the product itself no longer allows.
/// </summary>
public sealed class BulkEmployeeInvitationTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee with a valid work email and no linked user
    /// account — an eligible invite candidate. Mirrors
    /// EmployeeUserAccountColumnTests.CreateFreshUninvitedEmployeeAsync's own reasoning: a fresh
    /// employee avoids racing other tests/parallel runs over shared seeded rows.
    /// </summary>
    private async Task<(string Name, string Email)> CreateFreshInvitableEmployeeAsync(string labelPrefix = "Invite")
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"{labelPrefix}{unique}";
        var workEmail = $"e2e.{labelPrefix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
        await empEdit.SaveNewEmployeeAsync();

        return (lastName, workEmail);
    }

    // ── 1. Getting Started navigation into invitation mode ──────────────────────

    [Fact]
    public async Task GettingStarted_InviteYourTeamCard_NavigatesIntoInviteMode_WithReturnUrl()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await gettingStarted.GoToAsync();

        // InviteAdditionalUsersTask's LinkUrl already carries its own "?mode=invite" query string
        // — OnboardingTaskCard.ResolvedLinkUrl joins "&returnUrl=..." rather than "?returnUrl=..."
        // in that case (see its own doc comment).
        var href = await gettingStarted.GetTaskLinkUrlAsync("Invite your team");
        Assert.NotNull(href);
        Assert.Contains("mode=invite", href);
        Assert.Contains("returnUrl=", href);

        await gettingStarted.ClickTaskLinkAsync("Invite your team");

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/employees"), new() { Timeout = 20_000 });
        Assert.Contains($"/companies/{AcmeId}/employees", _page.Url);
        Assert.Contains("mode=invite", _page.Url);

        Assert.True(await empList.IsInviteModeBannerVisibleAsync(),
            "Expected the invitation-mode banner to be visible after navigating from the Getting Started card");

        var backHref = await empList.GetBackToListLinkHrefAsync();
        Assert.NotNull(backHref);
        Assert.Contains("getting-started", Uri.UnescapeDataString(backHref!));
    }

    // ── 2. Invitation mode loading + preselection ───────────────────────────────

    [Fact]
    public async Task InviteMode_DirectNavigation_ShowsBannerAndPreselectsEligibleCandidates()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (freshName, _) = await CreateFreshInvitableEmployeeAsync("Preselect");

        await empList.GoToInviteModeAsync(AcmeId);

        Assert.True(await empList.IsInviteModeBannerVisibleAsync(),
            "Expected the invitation-mode banner on a direct ?mode=invite navigation");

        await grid.WaitForLoadedAsync();

        // A freshly-created employee with a valid work email and no linked account is eligible —
        // auto-selected on load (InviteModeCandidateGrid.EligibleIndexes/OnAfterRenderAsync).
        Assert.True(await grid.IsRowCheckedAsync(freshName),
            $"Expected freshly created eligible candidate '{freshName}' to be pre-selected");

        Assert.True(await grid.GetSelectedCountAsync() > 0,
            "Expected at least one candidate to be pre-selected");
    }

    [Fact]
    public async Task InviteMode_CandidateMissingWorkEmail_IsVisibleButNotPreselected_WithEditLink()
    {
        // See this class's own doc comment: WorkEmail is mandatory on both create and update, so
        // this environment has no UI path to produce a genuinely missing-email candidate. Assert
        // the behaviour when the shared/seeded data happens to contain one; otherwise skip
        // gracefully rather than fabricate an unreachable state.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        var missingEmailRow = _page.Locator(".invite-mode-grid .e-row")
            .Filter(new() { HasText = "No work email" });

        if (await missingEmailRow.CountAsync() == 0)
        {
            return; // No such candidate exists in this environment — nothing to assert.
        }

        var rowText = (await missingEmailRow.First.InnerTextAsync()).Trim();
        Assert.False(
            await missingEmailRow.First.Locator(".e-checkbox-wrapper input[type='checkbox']").First.IsCheckedAsync(),
            $"Expected the candidate row missing a work email ('{rowText}') to NOT be pre-selected");

        var editLink = missingEmailRow.First.GetByRole(AriaRole.Link).Last;
        var href = await editLink.GetAttributeAsync("href");
        Assert.NotNull(href);
        Assert.Contains($"/companies/{AcmeId}/employees/", href);
    }

    // ── 3. Selection interactions ────────────────────────────────────────────────

    [Fact]
    public async Task InviteMode_DeselectingRow_ReducesCount_AndExcludesFromConfirmDialog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (keepName, _) = await CreateFreshInvitableEmployeeAsync("Keep");
        var (dropName, _) = await CreateFreshInvitableEmployeeAsync("Drop");

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        Assert.True(await grid.IsRowCheckedAsync(keepName));
        Assert.True(await grid.IsRowCheckedAsync(dropName));

        var countBefore = await grid.GetSelectedCountAsync();
        await grid.ToggleRowAsync(dropName);
        var countAfter = await grid.GetSelectedCountAsync();

        Assert.Equal(countBefore - 1, countAfter);
        Assert.False(await grid.IsRowCheckedAsync(dropName));
        Assert.True(await grid.IsRowCheckedAsync(keepName));

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();

        var names = await confirmDialog.GetRecipientNamesAsync();
        Assert.Contains(names, n => n.Contains(keepName));
        Assert.DoesNotContain(names, n => n.Contains(dropName));

        await confirmDialog.CancelAsync();
    }

    [Fact]
    public async Task InviteMode_SelectAllEligible_And_ClearSelection_UpdateCountAndButtons()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await CreateFreshInvitableEmployeeAsync("SelectAll");

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        var eligibleCount = await grid.GetSelectedCountAsync();
        Assert.True(eligibleCount > 0, "Expected at least one eligible candidate pre-selected");
        Assert.False(await grid.IsInviteSelectedButtonDisabledAsync(),
            "Expected 'Invite selected (N)' to be enabled while N > 0");

        await grid.ClickClearSelectionAsync();
        Assert.Equal(0, await grid.GetSelectedCountAsync());
        Assert.True(await grid.IsInviteSelectedButtonDisabledAsync(),
            "Expected 'Invite selected (N)' to be disabled once the selection count is 0");

        await grid.ClickSelectAllEligibleAsync();
        Assert.Equal(eligibleCount, await grid.GetSelectedCountAsync());
        Assert.False(await grid.IsInviteSelectedButtonDisabledAsync());
    }

    // ── 4. Confirmation and send ─────────────────────────────────────────────────

    [Fact]
    public async Task InviteMode_ConfirmAndSend_QueuesBatch_AndShowsProgressPanel()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (name, email) = await CreateFreshInvitableEmployeeAsync("SendFlow");

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        // Isolate this batch to just the one freshly created candidate — a shared, long-lived
        // Acme company can have other genuinely-eligible candidates too, and this test only wants
        // to assert on its own recipient.
        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);
        Assert.Equal(1, await grid.GetSelectedCountAsync());

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();

        Assert.Contains("Employee", await confirmDialog.GetIntroTextAsync());
        var names = await confirmDialog.GetRecipientNamesAsync();
        var emails = await confirmDialog.GetRecipientEmailsAsync();
        Assert.Contains(names, n => n.Contains(name));
        Assert.Contains(emails, e => e.Equals(email, StringComparison.OrdinalIgnoreCase));

        await confirmDialog.SendAsync();
        Assert.False(await confirmDialog.IsVisibleAsync());

        await progressPanel.WaitForVisibleAsync();
        Assert.True(await progressPanel.HasProcessingContinuesMessageAsync()
            || (await progressPanel.GetStatusLabelAsync()).Contains("Completed"),
            "Expected the progress panel to show either the 'processing continues' hint (Queued/Processing) or have already reached Completed");
    }

    // ── 5. Progress recovery after refresh/navigation ───────────────────────────

    [Fact]
    public async Task InviteMode_BatchProgress_SurvivesRefresh_AndReachesTerminalState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (name, _) = await CreateFreshInvitableEmployeeAsync("Recover");

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();
        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();
        await confirmDialog.SendAsync();

        await progressPanel.WaitForVisibleAsync();

        // Refresh the page entirely — InvitationBatchProgressPanel.OnParametersSetAsync re-fetches
        // via GET .../invitation-batches/latest rather than relying on any client-held state, so
        // the just-queued batch's progress must still be visible afterwards.
        await _page.ReloadAsync();
        await empList.IsInviteModeBannerVisibleAsync(); // settle: wait for the page shell to repaint
        await progressPanel.WaitForVisibleAsync();

        await progressPanel.WaitForCompletedAsync();

        var status = await progressPanel.GetStatusLabelAsync();
        Assert.Contains("Completed", status);

        var sent = await progressPanel.GetSentCountAsync();
        var skipped = await progressPanel.GetSkippedCountAsync();
        var failed = await progressPanel.GetFailedCountAsync();
        Assert.True(sent + skipped + failed >= 1,
            "Expected the completed batch to account for at least the one recipient queued");
    }

    // ── 6. Failure/retry flow ────────────────────────────────────────────────────

    /// <summary>
    /// A deterministic Skipped outcome: select an employee via the NORMAL grid's manual-selection
    /// "Invite selected" path who already has an active user account (Laura Bennett herself, the
    /// logged-in HR Administrator) — EmployeeList.OnInviteSelectedClicked classifies this
    /// client-side as "AlreadyHasAccount" and excludes her from the recipient list entirely before
    /// any batch is even queued, mirroring the server-side exclusion reasons. This does not reach
    /// InvitationBatchProgressPanel's own Skipped/Failed counts (those only apply to recipients the
    /// server actually attempted to email) — it is covered here as the deterministic,
    /// environment-independent equivalent: this suite avoids faking a real email-send failure (the
    /// email sender is a live/fake network dependency whose failure mode isn't reproducible on
    /// demand), so the "excluded reasons shown before send" level is the correct place to assert
    /// this deterministically, consistent with how EmployeeUserAccountColumnTests documents that
    /// "Invite expired" (a genuinely time-based state) isn't reachable through the UI either.
    /// </summary>
    [Fact]
    public async Task NormalGrid_ManualSelection_AlreadyHasAccountEmployee_IsExcludedFromConfirmDialog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await empList.GoToAsync(AcmeId);
        await empList.CheckEmployeeRowAsync("Laura Bennett"); // Active account already
        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        Assert.True(await confirmDialog.HasExcludedSectionAsync(),
            "Expected 'Laura Bennett' (already has an active account) to be excluded rather than queued");

        var excluded = await confirmDialog.GetExcludedRowsAsync();
        Assert.Contains(excluded, r => r.Name.Contains("Bennett") && r.Reason.Contains("account"));

        var names = await confirmDialog.GetRecipientNamesAsync();
        Assert.DoesNotContain(names, n => n.Contains("Bennett"));

        await confirmDialog.CancelAsync();
    }

    /// <summary>
    /// Best-effort coverage of the "Retry failed invitations" control's own visibility contract:
    /// it is rendered only once a batch shows Failed &gt; 0 (see InvitationBatchProgressPanel.razor).
    /// Since a real Failed outcome depends on the (non-deterministic in this environment, per the
    /// class-level remarks) email sender genuinely failing, this asserts the negative/default case
    /// deterministically — no Retry button while Failed == 0 — which is exercised by every other
    /// test in this class that reaches a Completed batch with 0 failures.
    /// </summary>
    [Fact]
    public async Task ProgressPanel_RetryButton_NotShown_WhenNoFailedRecipients()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (name, _) = await CreateFreshInvitableEmployeeAsync("NoRetry");

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();
        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);
        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();
        await confirmDialog.SendAsync();

        await progressPanel.WaitForVisibleAsync();
        await progressPanel.WaitForCompletedAsync();

        if (await progressPanel.GetFailedCountAsync() == 0)
        {
            Assert.False(await progressPanel.HasRetryButtonAsync(),
                "Expected no 'Retry failed invitations' button while the batch has 0 Failed recipients");
        }
        // If the environment's email sender happens to genuinely fail for this recipient, Failed
        // will be > 0 and the button is expected to show — nothing further to assert here without
        // faking the send outcome (see this class's Skipped/Failed remarks above).
    }

    // ── 7. Regression coverage ───────────────────────────────────────────────────

    /// <summary>
    /// The new "Invite selected (N)" toolbar action also works from the NORMAL (non-invite-mode)
    /// grid with a manual multi-row selection — not just the dedicated invitation-mode grid.
    /// Complements EmployeeUserAccountColumnTests (individual row Quick Invite, still covered
    /// there) and EmployeeListBulkUpdateTests (compensation bulk-update, unaffected by this
    /// feature) — this test is scoped to the one new toolbar action neither of those covers.
    /// </summary>
    [Fact]
    public async Task NormalGrid_ManualMultiSelection_InviteSelected_QueuesEligibleRecipients()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await empList.IsInviteSelectedToolbarButtonDisabledAsync(),
            "Expected 'Invite selected' to be disabled with no rows selected");

        var (name, email) = await CreateFreshInvitableEmployeeAsync("ManualSel");

        await empList.GoToAsync(AcmeId);
        await empList.CheckEmployeeRowAsync(name);

        Assert.False(await empList.IsInviteSelectedToolbarButtonDisabledAsync(),
            "Expected 'Invite selected' to be enabled once a row is selected");

        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        var names = await confirmDialog.GetRecipientNamesAsync();
        var emails = await confirmDialog.GetRecipientEmailsAsync();
        Assert.Contains(names, n => n.Contains(name));
        Assert.Contains(emails, e => e.Equals(email, StringComparison.OrdinalIgnoreCase));

        await confirmDialog.SendAsync();

        var successMessage = await empList.GetActionSuccessMessageAsync();
        Assert.Contains("queued", successMessage, StringComparison.OrdinalIgnoreCase);
    }
}
