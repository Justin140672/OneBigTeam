using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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
        var direct = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "TeamDirect", managerId: NinaId, activate: true);
        var indirect = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "TeamIndirect", managerId: direct.Id, activate: true);
        var indirectName = indirect.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var profile = new TeamMemberProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await dashboard.GoToAsync();

        var directNames = await dashboard.GetMyTeamMemberNamesAsync();
        Assert.DoesNotContain(directNames, n => n.Contains(indirectName, StringComparison.OrdinalIgnoreCase));

        await profile.GoToAsync(AcmeId, indirect.Id);
        Assert.True(await profile.IsProfileVisibleAsync(),
            $"Expected Nina (manager of {direct.FullName}, who manages {indirectName}) to be authorized for this indirect report's team-view.");
        Assert.Contains(indirectName, await profile.GetDisplayNameAsync());
    }

    [Fact]
    public async Task Manager_With_MoreThanEightReports_CanReach_NinthReport_ViaViewAllTeam()
    {
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
    public async Task ViewAllTeam_RosterPage_ScopeSwitcher_ExposesPressedState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await roster.GoToAsync(AcmeId);
        await roster.WaitForLoadedAsync();

        var direct = _page.GetByRole(AriaRole.Button, new() { Name = "Direct Reports" });
        var all = _page.GetByRole(AriaRole.Button, new() { Name = "All Reports" });

        await Assertions.Expect(direct).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(all).ToHaveAttributeAsync("aria-pressed", "false");

        await roster.SetScopeAsync(true);

        await Assertions.Expect(direct).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(all).ToHaveAttributeAsync("aria-pressed", "true");
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
        await roster.ExpectRowCountAsync(1);
        var afterSearch = await roster.GetRowNamesAsync();

        Assert.All(afterSearch, n => Assert.Contains(uniqueName, n, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(afterSearch, n => n.Contains(uniqueName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnrelatedManager_DeepLinking_ReceivesForbiddenState()
    {
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
        Assert.DoesNotContain("tom.williams@hotmail.com", pageText);
    }

    [Fact]
    public async Task HrAdministrator_FullEmployeePage_RemainsUnchanged()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await empEdit.GoToAsync(AcmeId, TomId);

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
