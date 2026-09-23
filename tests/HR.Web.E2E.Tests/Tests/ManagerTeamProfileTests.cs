using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

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
///   - Laura Bennett (30000000-0000-0000-0000-000000000005) — HR Administrator, used only via the
///     dev-persona API session to seed extra reports for the "more than 8" / indirect-hierarchy
///     tests, mirroring EmployeeNotesTabTests.SeedNotesAsync's pattern.
/// </summary>
public sealed class ManagerTeamProfileTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid TomId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string JamesEmail = "james.okafor@acme.example";
    private const string DavidEmail = "david.park@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    // Reference data reused by every employee this file creates via the API (see
    // EmployeesModule.SeedEmployeesAsync — Engineering dept / London office / QA Engineer
    // position / Permanent employment type, the same combination the E2E test pool itself uses).
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-00000000000B");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");

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
        // Give Tom Williams (James's direct report) a report of his own, making that new employee
        // an INDIRECT report of James — the scenario "All Reports" / hierarchy is meant to surface.
        var (indirectId, indirectName) = await CreateEmployeeViaApiAsync(managerId: TomId, "IndirectA");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        // Direct-reports-only preview should NOT include the indirect report.
        var directNames = await dashboard.GetMyTeamMemberNamesAsync();
        Assert.DoesNotContain(directNames, n => n.Contains(indirectName, StringComparison.OrdinalIgnoreCase));

        // Deep-link straight to the team-view route for the indirect report — proves the API
        // authorizes indirect hierarchy, not just the dashboard preview's direct-only default.
        await profile.GoToAsync(AcmeId, indirectId);
        Assert.True(await profile.IsProfileVisibleAsync(),
            $"Expected James (manager of Tom, who manages {indirectName}) to be authorized for this indirect report's team-view.");
        Assert.Contains(indirectName, await profile.GetDisplayNameAsync());
    }

    [Fact]
    public async Task Manager_With_MoreThanEightReports_CanReach_NinthReport_ViaViewAllTeam()
    {
        // 9 fresh direct reports for James — one more than the dashboard's 8-card preview cap.
        var created = new List<(Guid Id, string Name)>();
        for (var i = 1; i <= 9; i++)
            created.Add(await CreateEmployeeViaApiAsync(managerId: JamesId, $"Overflow{i}"));

        var ninth = created[8];

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
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
        var (_, uniqueName) = await CreateEmployeeViaApiAsync(managerId: JamesId, $"Searchable{Guid.NewGuid():N}"[..20]);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
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
        // GetEmployee endpoint and EmployeeEdit.razor page are unaffected by this feature.
        Assert.Contains("Tom", await _page.ContentAsync());
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

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh employee directly via POST /api/companies/{companyId}/employees, the same
    /// endpoint the New Employee form itself calls — used here only as fast *arrange* (this file
    /// is testing manager team-view authorization/navigation, not employee creation), mirroring
    /// EmployeeNotesTabTests.SeedNotesAsync's rationale for going through the API rather than the
    /// full multi-combobox creation form. Uses Laura Bennett's (HR Administrator) dev-persona
    /// session, the only role permitted to create employees or assign a manager directly at
    /// creation time.
    /// </summary>
    private async Task<(Guid Id, string Name)> CreateEmployeeViaApiAsync(Guid managerId, string lastNameSuffix)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        HttpResponseMessage? sessionResponse = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{LauraUserId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }
        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{{userId}} to succeed, got {sessionResponse.StatusCode}.");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var firstName = "E2E";
        var lastName = $"Team{lastNameSuffix}{unique}";

        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId,
                managerId,
                firstName,
                lastName,
                workEmail = $"e2e.team.{unique}@acme.example",
                startDate = "2026-03-01",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"E2E-TEAM-{unique}",
                employmentTypeId = EmploymentTypeId,
                hasSystemAccess = true,
            });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(created);

        // CreateEmployeeHandler always creates new employees as EmploymentStatus.Draft (see
        // Employee.Create), regardless of StartDate — there is no "create as Active" option via
        // this endpoint. GetMyTeamHandler (My Team widget / roster) only counts Status == Active
        // employees, so a Draft employee is invisible there even with a past start date. Activate
        // it via the same PUT .../employment endpoint the Employment tab's own Save uses.
        // UpdateEmploymentDetailsValidator.RequireLoadedVersion() makes ExpectedVersion mandatory
        // (Ticket 2 optimistic concurrency) despite the request record's own comment suggesting
        // null is fine for "standalone callers" — it isn't, for this endpoint. Load the just-
        // created record's real Version via GetEmployee first.
        var getResponse = await http.GetAsync($"/api/companies/{AcmeId}/employees/{created!.Id}");
        getResponse.EnsureSuccessStatusCode();
        var currentEmployee = await getResponse.Content.ReadFromJsonAsync<VersionPayload>();
        Assert.NotNull(currentEmployee);

        var activateResponse = await http.PutAsJsonAsync(
            $"/api/companies/{AcmeId}/employees/{created.Id}/employment",
            new
            {
                companyId = AcmeId,
                id = created.Id,
                employeeNumber = $"E2E-TEAM-{unique}",
                employmentTypeId = EmploymentTypeId,
                status = "Active",
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId,
                managerId,
                startDate = "2026-03-01",
                expectedVersion = currentEmployee!.Version,
            });
        activateResponse.EnsureSuccessStatusCode();

        return (created.Id, $"{firstName} {lastName}");
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);
    private sealed record IdPayload(Guid Id);
    private sealed record VersionPayload(int Version);
}
