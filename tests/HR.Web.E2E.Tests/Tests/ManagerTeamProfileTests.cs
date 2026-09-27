using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Manager team-profile journey: GetEmployeeTeamView (API, manager hierarchy only) +
/// TeamMemberProfile.razor (read-only profile) + MyTeamRoster.razor ("View all team" full list,
/// every non-former-employee direct/indirect report) + the "View profile" entry points on
/// MyTeamWidget.razor's dashboard cards.
///
/// Uses seeded personas (see ManagerDashboardTests's own remarks for the same pool):
///   - James Okafor (james.okafor@acme.example) — Manager only, manages Tom Williams directly.
///   - Tom Williams (tom.williams@acme.example) — plain Employee, reports to James.
///   - David Park (david.park@acme.example) — HrAdministrator + Manager, manages Emma Jones and
///     Carlos Rivera directly, NOT related to James's hierarchy — used as the "unrelated manager"
///     denial case.
///   - Nina Patel (nina.patel@acme.example, SeededE2eEmployees.DedicatedManagerNinaPatelId) — an
///     E2E-only Manager persona with no seeded reports, used by every test here that GROWS a
///     manager's team at runtime (the "more than 8" overflow, the searchable roster entry, the
///     indirect-hierarchy report). Those tests previously added 10 Active direct reports to James
///     and gave Tom a report of his own; their "E2E Team…" last names sort before "Williams", so
///     Tom was pushed out of James's 8-card My Team preview (breaking ManagerDashboardTests'
///     MyTeamWidget_* tests and this class's own Tom-card tests), and each new report also added
///     overdue onboarding/probation tasks and notifications to James's shared attention queue,
///     task list and notification bell.
///   - Laura Bennett — HR Administrator, used only via the dev-persona API session
///     (E2eEmployeeApi) to create those fresh reports.
/// </summary>
public sealed class ManagerTeamProfileTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid NinaId = SeededE2eEmployees.DedicatedManagerNinaPatelId;

    private const string JamesEmail = "james.okafor@acme.example";
    private const string NinaEmail = SeededE2eEmployees.DedicatedManagerNinaPatelEmail;
    private const string DavidEmail = "david.park@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task Manager_CanOpen_DirectReport_FromDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();
        await dashboard.ClickViewProfileForTeamMemberAsync("Tom Williams");

        await profile.WaitForSettledAsync();
        Assert.True(await profile.IsProfileVisibleAsync());
        Assert.Contains("Tom Williams", await profile.GetDisplayNameAsync());
    }

    [Fact]
    public async Task Manager_CanSwitchToAllReports_AndOpenIndirectReport()
    {
        // A fresh direct report of Nina's, who in turn manages a fresh report of their own — that
        // second employee is an INDIRECT report of Nina, the scenario "All Reports" / hierarchy is
        // meant to surface. Both are this test's own employees (see class remarks).
        var direct = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "TeamDirect", managerId: NinaId, activate: true);
        var indirect = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "TeamIndirect", managerId: direct.Id, activate: true);
        var indirectName = indirect.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await dashboard.GoToAsync();

        // Direct-reports-only preview should NOT include the indirect report.
        var directNames = await dashboard.GetMyTeamMemberNamesAsync();
        Assert.DoesNotContain(directNames, n => n.Contains(indirectName, StringComparison.OrdinalIgnoreCase));

        // Deep-link straight to the team-view route for the indirect report — proves the API
        // authorizes indirect hierarchy, not just the dashboard preview's direct-only default.
        await profile.GoToAsync(AcmeId, indirect.Id);
        Assert.True(await profile.IsProfileVisibleAsync(),
            $"Expected Nina (manager of {direct.FullName}, who manages {indirectName}) to be authorized for this indirect report's team-view.");
        Assert.Contains(indirectName, await profile.GetDisplayNameAsync());
    }

    [Fact]
    public async Task Manager_With_MoreThanEightReports_CanReach_NinthReport_ViaViewAllTeam()
    {
        // 9 fresh direct reports for Nina — one more than the dashboard's 8-card preview cap. (Other
        // tests in this class may add further reports of Nina's concurrently; this test only relies
        // on "more than 8" and on its own ninth report being reachable, both of which still hold.)
        var created = new List<(Guid Id, string Name)>();
        for (var i = 1; i <= 9; i++)
        {
            var report = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, $"TeamOverflow{i}", managerId: NinaId, activate: true);
            created.Add((report.Id, report.FullName));
        }

        var ninth = created[8];

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await dashboard.GoToAsync();

        // The preview overflow notice appears once there are more than 8 direct reports.
        await dashboard.GetMyTeamMemberNamesAsync();
        Assert.True(await dashboard.HasViewAllTeamInlineLinkAsync(),
            "Expected the dashboard's 8-card preview to show a 'view all team' overflow notice with 9 direct reports.");

        await dashboard.ClickViewAllTeamAsync();
        await roster.WaitForLoadedAsync();

        var names = await roster.GetRowNamesAsync();
        Assert.Contains(names, n => n.Contains(ninth.Name, StringComparison.OrdinalIgnoreCase));

        await roster.ClickViewProfileAsync(ninth.Id);
        await profile.WaitForSettledAsync();
        Assert.True(await profile.IsProfileVisibleAsync());
        Assert.Contains(ninth.Name, await profile.GetDisplayNameAsync());
    }

    [Fact]
    public async Task ViewAllTeam_RosterPage_ReachableDirectly_AndSearchNarrowsResults()
    {
        var searchable = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "TeamSearchable", managerId: NinaId, activate: true);
        var uniqueName = searchable.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await roster.GoToAsync(AcmeId);

        var beforeSearch = await roster.RowCountAsync();
        Assert.True(beforeSearch > 0, "Expected at least one direct report in the roster.");

        await roster.SearchAsync(uniqueName);
        var afterSearch = await roster.GetRowNamesAsync();

        Assert.All(afterSearch, n => Assert.Contains(uniqueName, n, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(afterSearch, n => n.Contains(uniqueName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnrelatedManager_DeepLinking_ReceivesForbiddenState()
    {
        // NOT David Park: he's a Manager (Emma Jones/Carlos Rivera, not Tom Williams — genuinely
        // unrelated hierarchically) but he's ALSO an HR Administrator, and
        // EmployeesResourceAuthorizer.CanViewAsManagerAsync's shared IAM-07 check grants company-
        // wide access to any HR Administrator before it ever reaches the hierarchy check (see its
        // own remarks: "HR Administrators still pass here too... which is harmless") — so David
        // always sees Tom's team-view regardless of hierarchy, by design. Use Marcus Diallo
        // instead: an Acme employee with no HrAdministrator role and no place in Tom's reporting
        // chain (Tom → James Okafor → Sarah Chen), so only the hierarchy check applies and it
        // correctly denies him.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await profile.GoToAsync(AcmeId, TomId);

        Assert.True(await profile.IsForbiddenAsync(),
            "Expected Marcus Diallo (not Tom Williams's manager, not an HR Administrator) to see the forbidden state deep-linking to Tom's team-view.");
        Assert.False(await profile.IsProfileVisibleAsync());
    }

    [Fact]
    public async Task Employee_CannotUse_ManagerRoute_ForOwnRecord()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        Assert.True(await profile.IsForbiddenAsync(),
            "Expected Tom Williams to be forbidden from his own /team-view route — self-access goes through GetEmployee/his own profile page, never this manager-only endpoint.");
    }

    [Fact]
    public async Task NoRestrictedFields_AppearInTeamViewDom()
    {
        // NOTE: this app is Blazor Server — EmployeeService.GetEmployeeTeamViewAsync calls the HR
        // API via a server-side HttpClient (see EmployeeService.cs), entirely server-to-server. The
        // browser never sees that request/response as a network event, so a Playwright
        // page.Response listener can never observe it — teamViewResponseBody would be null on every
        // run, not just a flaky one. The only client-observable surface for "did a restricted field
        // leak" is the rendered DOM itself, which is what this test now checks exclusively.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();
        await dashboard.GetMyTeamMemberNamesAsync();
        await dashboard.ClickViewProfileForTeamMemberAsync("Tom Williams");
        await profile.WaitForSettledAsync();

        var pageText = await profile.GetPageTextAsync();
        Assert.DoesNotContain("tom.williams@hotmail.com", pageText); // Tom's PersonalEmail — never in the DOM.
    }

    [Fact]
    public async Task HrAdministrator_FullEmployeePage_RemainsUnchanged()
    {
        // David Park is both Manager and HrAdministrator — his own full-record HR page must be
        // completely unaffected by the manager team-view split, for a report he isn't even
        // managing (proving this is HR-admin access via GetEmployee, not a hierarchy fallback).
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await empEdit.GoToAsync(AcmeId, TomId);

        // A successful load of the full HR edit page (not a forbidden/redirect state) proves the
        // GetEmployee endpoint and EmployeeEdit.razor page are unaffected by this feature. Wait for
        // the page's own employee header (EmployeeEdit.razor's "<h1>{FirstName} {LastName}</h1>",
        // rendered once GetEmployee has returned) rather than snapshotting the raw HTML: that
        // snapshot could be taken before the employee data had loaded — GoToAsync's old
        // combobox wait was satisfied by the layout's dev persona switcher alone.
        await Assertions.Expect(_page.Locator(".content-area h1").First)
            .ToContainTextAsync("Tom Williams", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task TeamMemberProfile_RendersAtNarrowViewport()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        await _page.SetViewportSizeAsync(375, 812);
        await profile.GoToAsync(AcmeId, TomId);

        Assert.True(await profile.IsProfileVisibleAsync(),
            "Expected the manager team-view profile to render at a narrow (mobile) viewport.");
        Assert.Contains("Tom Williams", await profile.GetDisplayNameAsync());
    }
}
