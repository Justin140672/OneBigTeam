using System.Text.RegularExpressions;
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
[Collection("E2E")]
public sealed class EmployeeTimelineTabTests(AppFixture fixture) : E2ETestBase(fixture)
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

        await CreateEmployeeAsync(empList, empEdit, startDateDdMmYyyy: "01/01/2020");

        // ── The employee's only entry so far is "Employee joined", dated by the start date ──
        await timeline.OpenAsync();

        var textsBeforeNote = await timeline.GetEntryTextsAsync();
        Assert.Single(textsBeforeNote);
        Assert.Contains("Employee joined", textsBeforeNote[0]);
        Assert.Contains("1 Jan 2020", textsBeforeNote[0]);
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

        await CreateEmployeeAsync(empList, empEdit, startDateDdMmYyyy: "01/01/2020");

        // Page size is 20 (see EmployeeTimelineTab.razor's PageSize const). 22 HR notes plus the
        // one pre-existing "Employee joined" entry give 23 total — 20 on the first page, 3 more
        // after "Load more".
        await empEdit.OpenNotesTabAsync();
        for (var i = 0; i < 22; i++)
        {
            await empEdit.ClickAddNoteAsync();
            await empEdit.SelectAddNoteCategoryAsync("General");
            await empEdit.FillAddNoteTextAsync($"Load more note {i} {Guid.NewGuid():N}");
            await empEdit.SubmitAddNoteDialogAsync();
        }

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

        await CreateEmployeeAsync(empList, empEdit, startDateDdMmYyyy: "01/01/2020");

        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();
        await wizard.SelectNewPositionProfileAsync("Senior Software Engineer");
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
        Assert.True(await _page.GetByRole(AriaRole.Tab, new() { Name = "Promotion History" })
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
    private async Task<(Guid Id, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string startDateDdMmYyyy)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Timeline{unique}";
        var workEmail = $"e2e.timeline{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync(startDateDdMmYyyy);
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Software Engineer");

        await empEdit.SaveNewEmployeeAsync();

        await empList.SearchAsync(lastName);
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, "/employees/([0-9a-fA-F-]{36})");
        Assert.True(match.Success, $"Could not parse the new employee's id from URL '{_page.Url}'");
        var id = Guid.Parse(match.Groups[1].Value);

        return (id, lastName);
    }
}
