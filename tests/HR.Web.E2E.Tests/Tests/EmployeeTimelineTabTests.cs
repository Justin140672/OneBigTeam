using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Timeline" tab (EmployeeTimelineTab.razor), shared between the HR-facing employee
/// edit page (EmployeeEdit.razor) and the self-service profile page (MyProfile.razor).
///
/// Timeline entries are only ever written by real domain-event handlers (see
/// HR.Modules.Employees.Features.CreateTimelineEntryOn* — e.g. creating an employee writes an
/// "Employee joined" entry, promoting one writes a "Promoted" entry, adding an HR note writes an
/// "HR note added" entry). There is no dev-seed shortcut and no direct DB access from this test
/// project, so each test that needs a controlled/known set of entries creates its own employee
/// via the UI and drives the relevant real actions (new-employee creation, the Promote wizard,
/// adding HR notes) to generate them — this also keeps each test's entry set isolated from
/// whatever other, unrelated E2E tests do to shared seeded employees (e.g. Tom Williams) elsewhere
/// in this suite.
///
/// Because EmployeeTimelineTab's data fetch runs server-side over the existing Blazor Server
/// SignalR circuit (see EmployeeTimelineTab page object's remarks), pagination assertions here
/// verify rendered effects rather than intercepting an outgoing browser HTTP request — there
/// isn't one to intercept.
///
/// Note: category/date-range filtering was removed from both the API and this tab, so there is
/// no E2E-reachable scenario producing a genuinely empty timeline (every employee gets at least
/// an "Employee joined" entry) — the empty-state rendering path (HrEmptyState when the response
/// has zero items) exists in EmployeeTimelineTab.razor but is currently unexercised by this suite.
/// </summary>
public sealed class EmployeeTimelineTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Timeline_ShowsEntriesNewestFirst_WithExpectedFields()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var timeline = new EmployeeTimelineTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        // ── The employee's only entry so far is "Employee joined", dated by the seeded start date ──
        await timeline.OpenAsync();

        var textsBeforeNote = await timeline.GetEntryTextsAsync();
        Assert.Single(textsBeforeNote);
        Assert.Contains("Employee joined", textsBeforeNote[0]);
        Assert.Contains("1 Mar 2026", textsBeforeNote[0]);
        // No performer is recorded for the "Employee joined" event — should fall back to "System".
        Assert.Contains("System", textsBeforeNote[0]);

        // ── Add an HR note (dated today) — should render above "Employee joined" ──
        var noteText = $"Timeline ordering check {Guid.NewGuid():N}";
        await empEdit.OpenNotesTabAsync();
        await empEdit.ClickAddNoteAsync();
        await empEdit.SelectAddNoteCategoryAsync("General");
        await empEdit.FillAddNoteTextAsync(noteText);
        await empEdit.SubmitAddNoteDialogAsync();

        await timeline.OpenAsync();
        var textsAfterNote = await timeline.GetEntryTextsAsync();

        Assert.Equal(2, textsAfterNote.Count);
        Assert.Contains("HR note added", textsAfterNote[0]);
        // Laura performed the note-adding action — her name should resolve as the performer.
        Assert.Contains("Laura", textsAfterNote[0]);
        Assert.Contains("Employee joined", textsAfterNote[1]);
    }

    [Fact]
    public async Task Timeline_LoadMore_AppendsEntries_WithoutFullPageReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var timeline = new EmployeeTimelineTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit, slot: 1);

        // Page size is 20 (see EmployeeTimelineTab.razor's PageSize const). 22 HR notes plus the
        // one pre-existing "Employee joined" entry give 23 total — 20 on the first page, 3 more
        // after "Load more". Only this test's OWN Timeline-pagination behaviour is under test here
        // — the Notes tab's dialog/UI is already covered by EmployeeNotesTabTests — so these 22
        // notes are seeded via a direct authenticated call to the same CreateEmployeeNote endpoint
        // the Add Note dialog itself calls (still a real handler round trip, still writes a real
        // "HR note added" timeline entry via the real domain event — not a DB-seeding shortcut,
        // see this file's own header comment) rather than 22 slow, repeated Syncfusion dialog
        // round-trips through the browser.
        await SeedNotesAsync(employeeId, count: 22);

        await empEdit.GoToAsync(AcmeId, employeeId);
        var urlBeforeLoadMore = _page.Url;

        await timeline.OpenAsync();

        Assert.Equal(20, await timeline.GetEntryCountAsync());
        Assert.True(await timeline.HasLoadMoreButtonAsync(),
            "Expected the 'Load more' button while 20 of 23 entries have been loaded");

        var firstPageTexts = await timeline.GetEntryTextsAsync();

        await timeline.ClickLoadMoreAsync(previousCount: 20);

        Assert.Equal(23, await timeline.GetEntryCountAsync());
        Assert.False(await timeline.HasLoadMoreButtonAsync(),
            "Expected 'Load more' to disappear once every entry has been loaded");

        // No full page reload happened — the URL is unchanged and the first page's entries are
        // still present in the DOM (appended to, not replaced).
        Assert.Equal(urlBeforeLoadMore, _page.Url);
        var allTexts = await timeline.GetEntryTextsAsync();
        foreach (var text in firstPageTexts)
            Assert.Contains(text, allTexts);
    }

    [Fact]
    public async Task Timeline_ShowsUpcomingBadge_ForFutureDatedEntry()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);
        var timeline = new EmployeeTimelineTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 2);

        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();
        // The pool employee's current profile is already "QA Engineer", so promote to a different
        // profile. "Software Engineer" is already occupied (Tom Williams) so it's already excluded
        // from VacancyDetail's "New Vacancy" Position Profile dropdown — adding this employee to it
        // changes nothing for the Recruitment E2E tests. "Senior Software Engineer" must stay free
        // for those tests, so it is deliberately not used here.
        await wizard.SelectNewPositionProfileAsync("Software Engineer");
        // A few days ahead of "today" — future enough to be after the run date, near enough not
        // to require guessing far into the future. See EmployeePromotionTabTests for the same
        // one-week-ahead convention used elsewhere in this suite.
        var futureEffectiveDate = DateTime.Today.AddDays(7).ToString("dd/MM/yyyy");
        await wizard.FillEffectiveDateAsync(futureEffectiveDate);
        await wizard.FillReasonAsync("Timeline upcoming-badge check");
        await wizard.ClickNextAsync();
        await wizard.ClickNextAsync();
        await wizard.ClickNextAsync();
        await wizard.SubmitAsync();

        Assert.False(await wizard.IsVisibleAsync());

        await timeline.OpenAsync();

        Assert.True(await timeline.EntryHasUpcomingBadgeAsync("Promoted"),
            "Expected the future-dated promotion entry to show the 'Upcoming' badge");
        Assert.False(await timeline.EntryHasUpcomingBadgeAsync("Employee joined"),
            "Did not expect the past-dated 'Employee joined' entry to show the 'Upcoming' badge");
    }

    [Fact]
    public async Task ViewDetails_NavigatesToDestinationTab_ForHr_ButNotRendered_ForSelfService()
    {
        // Tom Williams already has a real login and self-service profile access, so this test
        // reuses him rather than creating a fresh employee (self-service dev-auth login is only
        // established for seeded personas). A distinctive target position ("HR Manager", not used
        // elsewhere for Tom in this suite) keeps the new promotion's timeline summary
        // ("Promoted from ... to HR Manager.") unambiguous even if other tests have also promoted
        // Tom to a different position.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);
        var timeline = new EmployeeTimelineTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();
        await wizard.SelectNewPositionProfileAsync("HR Manager");
        await wizard.FillEffectiveDateAsync(DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy"));
        await wizard.FillReasonAsync("Timeline View Details check");
        await wizard.ClickNextAsync();
        await wizard.ClickNextAsync();
        await wizard.ClickNextAsync();
        await wizard.SubmitAsync();
        Assert.False(await wizard.IsVisibleAsync());

        // ── HR view (EmployeeEdit): "View details" is present and navigates to Promotion History ──
        await timeline.OpenAsync();
        Assert.True(await timeline.EntryHasViewDetailsLinkAsync("HR Manager"),
            "Expected 'View details' on the promotion entry for the HR admin viewer");

        await timeline.ClickViewDetailsAsync("HR Manager");
        await _page.WaitForSelectorAsync("[data-testid='promote-employee-btn']", new() { Timeout = 15_000 });
        Assert.True(await EmployeeEditPage.SectionTab(_page, "Promotion History")
            .GetAttributeAsync("aria-selected") == "true");

        // ── Self-service view (MyProfile): the same event type has no wired navigation callback ──
        var login2 = new LoginPage(_page, _fixture.WebBaseUrl);
        await login2.GoToAsync();
        await login2.LoginAsync("tom.williams@acme.example");

        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomWilliams);
        await timeline.OpenAsync();

        Assert.False(await timeline.EntryHasViewDetailsLinkAsync("HR Manager"),
            "Did not expect 'View details' on the promotion entry from the self-service profile, " +
            "since MyProfile.razor only wires OnNavigateToDocuments/OnNavigateToAcknowledgements");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a brand-new employee via the UI (HR admin must already be logged in) and leaves the
    /// browser on that employee's edit page. Returns the new employee's id (parsed from the
    /// resulting URL) and last name. A fresh employee guarantees the only pre-existing timeline
    /// entry is "Employee joined" (dated <paramref name="startDateDdMmYyyy"/>), regardless of what
    /// other E2E tests have done to shared seeded employees elsewhere in the suite.
    /// </summary>
    // Each of the three timeline scenarios below gets its own dedicated pre-seeded pool employee
    // (SeededE2eEmployees.Timeline[slot]) rather than paying the full New Employee form. A pool
    // member's only pre-existing timeline entry is "Employee joined" dated 2026-03-01 (its seeded
    // StartDate) — a NotStarted onboarding plan writes no timeline entry — so the "exactly one
    // entry to start" guarantee the old fresh-employee flow gave still holds. Each test mutates its
    // own employee (adds notes / a promotion), hence one distinct row (slot) per test.
    private async Task<(Guid Id, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.Timeline[slot];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return (seeded.EmployeeId, seeded.LastName);
    }

    /// <summary>
    /// Creates <paramref name="count"/> HR notes for <paramref name="employeeId"/> via direct
    /// authenticated calls to the same POST /api/companies/{companyId}/employees/{employeeId}/notes
    /// endpoint the Add Note dialog itself calls (HR.Modules.Employees' CreateEmployeeNote feature)
    /// — a real handler round trip that still writes a real "HR note added" timeline entry via the
    /// real domain event, not a DB-seeding shortcut (see this file's own header comment on why one
    /// was deliberately avoided). Only the transport changes: HTTP instead of 22 repeated Syncfusion
    /// dialog round-trips through the browser, which is what made this test's arrange phase slow.
    ///
    /// Laura (LauraEmail, already logged in via the UI for the rest of the test) is also used here
    /// as the note-creating actor, via the same dev-only "obtain a real Supabase session for a
    /// known persona" endpoint the persona switcher and DevAuthService.SwitchAsync use — see
    /// Program.cs's POST /api/dev/persona/{userId}.
    /// </summary>
    private async Task SeedNotesAsync(Guid employeeId, int count)
    {
        const string lauraUserId = "30000000-0000-0000-0000-000000000005";

        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var sessionResponse = await http.PostAsync($"/api/dev/persona/{lauraUserId}", content: null);
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        for (var i = 0; i < count; i++)
        {
            var response = await http.PostAsJsonAsync(
                $"/api/companies/{AcmeId}/employees/{employeeId}/notes",
                new
                {
                    CompanyId = AcmeId,
                    EmployeeId = employeeId,
                    Category = "General",
                    NoteText = $"Load more note {i} {Guid.NewGuid():N}",
                    IsImportant = false,
                });
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);
}
