using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the admin employee editor
/// (EmployeeEdit.razor). When a combined save fails because the employee record changed since it
/// was loaded (HTTP 409), the page shows an <c>alert alert-warning</c> banner
/// ("Someone else changed this employee's details while you were editing. Your changes have not
/// been saved.") plus a "Reload latest values" button that re-fetches the record, repopulates the
/// form and clears the banner.
///
/// The "second editor" is simulated with a second browser tab in the same authenticated context
/// (same HR admin persona — the banner wording is just "someone else"), which loads the same
/// employee and saves first, bumping Employee.Version and making the first tab's save stale.
///
/// Uses the dedicated pre-seeded pool employee <see cref="SeededE2eEmployees.ConcurrencyAdmin"/>
/// so no other parallel test class mutates it mid-run.
/// </summary>
public sealed class EmployeeEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = SeededE2eEmployees.AcmeCompanyId;
    private static readonly Guid EmployeeId = SeededE2eEmployees.ConcurrencyAdmin.EmployeeId;

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Editor_SaveAfterAnotherActorChangedEmployee_ShowsWarningBanner_ThenReloadRecovers()
    {
        var firstTabValue  = $"E2E First Tab {Guid.NewGuid():N}"[..24];
        var otherTabValue  = $"E2E Other Tab {Guid.NewGuid():N}"[..24];
        var finalValue     = $"E2E Final {Guid.NewGuid():N}"[..20];

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Tab 1: open the employee editor and start editing the Preferred Name ──
        await empEdit.GoToAsync(AcmeId, EmployeeId);
        await empEdit.FillPreferredNameAsync(firstTabValue);

        // ── Tab 2 (same context / persona): load the same employee and save a change first ──
        var otherPage    = await _context.NewPageAsync();
        try
        {
            var otherEditor = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEditor.GoToAsync(AcmeId, EmployeeId);
            await otherEditor.FillPreferredNameAsync(otherTabValue);
            await otherEditor.ClickSaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → warning banner, no navigation, input preserved ──
        await empEdit.ClickSaveExpectingConflictAsync();

        Assert.True(await empEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency warning banner after a stale save");
        Assert.Contains("/employees/", _page.Url);
        Assert.DoesNotContain("/view", _page.Url);
        Assert.Equal(firstTabValue, await empEdit.GetPreferredNameValueAsync());

        // ── Tab 1: "Reload latest values" clears the banner and shows the other actor's value ──
        await empEdit.ClickReloadLatestValuesAsync();

        Assert.False(await empEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the warning banner to clear after reloading latest values");
        Assert.Equal(otherTabValue, await empEdit.GetPreferredNameValueAsync());

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await empEdit.FillPreferredNameAsync(finalValue);
        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, EmployeeId);
        Assert.Equal(finalValue, await empEdit.GetPreferredNameValueAsync());
    }

    /// <summary>
    /// Ticket 2, item 5: the admin Employee Edit screen now saves the Profile tab and the
    /// Employment tab in ONE atomic transactional request
    /// (PUT /api/companies/{companyId}/employees/{id}/profile-and-employment) guarded by a single
    /// concurrency version. Previously two sequential PUTs could leave the profile committed while
    /// the employment PUT 409'd.
    ///
    /// This test edits BOTH a Profile-tab field (Preferred Name) and an Employment-tab field
    /// (HR Notes) in one unsaved session, has a second tab bump the version, then asserts on the
    /// conflicted save that NEITHER change was applied server-side and BOTH inputs still hold the
    /// first tab's unsaved values. "Reload latest values" then discards the local Notes edit and
    /// shows the fresh server state; re-editing both against the new version saves atomically.
    /// </summary>
    [Fact]
    public async Task Editor_AtomicProfileAndEmploymentSave_ConflictPreservesBothInputs_ThenReloadAndResaveSucceeds()
    {
        var firstPreferredName = $"E2E First {Guid.NewGuid():N}"[..20];
        var firstNotes         = $"E2E first-tab notes {Guid.NewGuid():N}";
        var otherPreferredName = $"E2E Other {Guid.NewGuid():N}"[..20];
        var finalPreferredName = $"E2E Final {Guid.NewGuid():N}"[..20];
        var finalNotes         = $"E2E final notes {Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Tab 1: open the employee, capture the original Notes, edit BOTH tabs, don't save ──
        await empEdit.GoToAsync(AcmeId, EmployeeId);
        var originalNotes = await empEdit.GetEmploymentNotesValueAsync();
        await empEdit.FillEmploymentNotesAsync(firstNotes);
        await empEdit.FillPreferredNameAsync(firstPreferredName);

        // ── Tab 2 (same context / persona): change Preferred Name and save first, bumping Version ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEditor = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEditor.GoToAsync(AcmeId, EmployeeId);
            await otherEditor.FillPreferredNameAsync(otherPreferredName);
            await otherEditor.ClickSaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: the combined save is now stale → warning banner, no navigation ──
        await empEdit.ClickSaveExpectingConflictAsync();

        Assert.True(await empEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency warning banner after a stale combined save");
        Assert.Contains("/employees/", _page.Url);
        Assert.DoesNotContain("/view", _page.Url);

        // Atomic: nothing was applied, so BOTH inputs still hold Tab 1's unsaved values.
        Assert.Equal(firstPreferredName, await empEdit.GetPreferredNameValueAsync());
        Assert.Equal(firstNotes, await empEdit.GetEmploymentNotesValueAsync());

        // ── Tab 1: "Reload latest values" → banner clears, form shows fresh server state ──
        await empEdit.ClickReloadLatestValuesAsync();

        Assert.False(await empEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the warning banner to clear after reloading latest values");
        Assert.Equal(otherPreferredName, await empEdit.GetPreferredNameValueAsync());
        // The explicit reload discards Tab 1's local Notes edit — back to the original server value.
        Assert.Equal(originalNotes, await empEdit.GetEmploymentNotesValueAsync());

        // ── Tab 1: re-edit both against the fresh version and save atomically ──
        await empEdit.FillEmploymentNotesAsync(finalNotes);
        await empEdit.FillPreferredNameAsync(finalPreferredName);
        // ClickSaveChangesAsync throws on any error banner and waits for the post-save navigation
        // away from the edit route — a successful atomic save.
        await empEdit.ClickSaveChangesAsync();

        // ── Reload and assert both fields persisted ──
        await empEdit.GoToAsync(AcmeId, EmployeeId);
        Assert.Equal(finalPreferredName, await empEdit.GetPreferredNameValueAsync());
        Assert.Equal(finalNotes, await empEdit.GetEmploymentNotesValueAsync());
    }

    /// <summary>
    /// Bug (b): once a concurrency banner is showing, editing a field to an invalid value (here:
    /// clearing the required "Last Name") must clear the stale banner so it doesn't linger next to
    /// the new validation errors. Driven by EditPageBase.OnValidationStateChanged.
    /// Shares the pool employee with the test above — xUnit runs the two serially (no intra-class
    /// parallelism), and each test starts from a fresh page load, so there is no shared mutable state.
    /// </summary>
    [Fact]
    public async Task Editor_MakingFieldInvalidAfterConflict_ClearsWarningBanner()
    {
        var firstTabValue = $"E2E First {Guid.NewGuid():N}"[..20];
        var otherTabValue = $"E2E Other {Guid.NewGuid():N}"[..20];

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, EmployeeId);
        await empEdit.FillPreferredNameAsync(firstTabValue);

        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEditor = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEditor.GoToAsync(AcmeId, EmployeeId);
            await otherEditor.FillPreferredNameAsync(otherTabValue);
            await otherEditor.ClickSaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        await empEdit.ClickSaveExpectingConflictAsync();
        Assert.True(await empEdit.IsConcurrencyWarningVisibleAsync());

        // Make the form invalid — the banner should disappear.
        await empEdit.MakeDetailsFormInvalidAsync();

        Assert.False(await empEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the concurrency banner to clear once the form became invalid after a conflict");
    }
}
