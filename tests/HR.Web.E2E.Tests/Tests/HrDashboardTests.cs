using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the HR-only dashboard (src/HR.Web/Components/Pages/Dashboards/HrDashboard.razor),
/// reached via "/dashboard/hr". The page guards on Session.IsHrAdministrator and redirects any
/// other role to Session.MyProfileUrl, so a non-HR-administrator can never see any of the
/// widgets below at all — unlike the pre-restructure single dashboard, where widgets were hidden
/// individually per-widget while the page itself stayed reachable.
///
/// Widgets covered: HeadcountByDepartmentChart, HrInboxWidget, LeaveRequestsWidget,
/// UpcomingProbationReviewsWidget, the sickness trio (CurrentSicknessAbsenceWidget,
/// OverdueReturnToWorkReviewsWidget, MissingFitNotesWidget), ComplianceDocumentExpiryWidget
/// ("Document Compliance"), and RecentEmployeeChangesWidget.
///
/// Uses seeded personas: Laura Bennett (HR Administrator only) and Tom Williams (plain Employee).
/// </summary>
[Collection("E2E")]
public sealed class HrDashboardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    private const string CurrentSicknessAbsenceTitle = "Current Sickness Absence";
    private const string OverdueReturnToWorkTitle    = "Overdue Return-to-Work Reviews";
    private const string MissingFitNotesTitle        = "Missing Fit Notes";

    [Fact]
    public async Task NonHrAdministrator_IsRedirectedAway_FromHrDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/hr");

        // Tom is a plain Employee — HrDashboard.razor's guard bounces him to his own profile
        // (AppSession.MyProfileUrl) before any widget renders.
        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}/profile"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/dashboard/hr", _page.Url);
    }

    [Fact]
    public async Task HrAdministrator_SeesAllHrWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Headcount by Department"));
        Assert.True(await dashboard.HasWidgetAsync("HR Inbox"));
        Assert.True(await dashboard.HasWidgetAsync("Leave Requests"));
        Assert.True(await dashboard.HasWidgetAsync("Upcoming Probation Reviews"));
        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(OverdueReturnToWorkTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));
        Assert.True(await dashboard.HasWidgetAsync("Document Compliance"));
        Assert.True(await dashboard.HasWidgetAsync("Recent Employee Changes"));
    }

    [Fact]
    public async Task HeadcountByDepartmentChart_Loads()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();
    }

    [Fact]
    public async Task HrInboxWidget_ViewAll_NavigatesToHrInbox()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickHrInboxViewAllAsync();

        Assert.Contains("/hr/inbox", _page.Url);
    }

    [Fact]
    public async Task LeaveRequestsWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Leave Requests");
        // No assertion on specific content — just that the widget resolves to either items or
        // the empty state, exercised via GetLeaveRequestEmployeeNamesAsync's internal wait.
        await dashboard.GetLeaveRequestEmployeeNamesAsync();
    }

    // ── Upcoming Probation Reviews Widget ────────────────────────────────────
    // Also appears on ManagerDashboard (gated on CanManageEmployees || IsManager) — full
    // regression coverage, including the click-through, lives here since this was the original
    // widget's home in UpcomingProbationReviewsWidgetTests.cs; ManagerDashboardTests only checks
    // that it is present for a manager persona too.

    [Fact]
    public async Task UpcomingProbationReviewsWidget_ShowsCarlosRivera()
    {
        // Depends on the seeded "Carlos Rivera" probation record (company: Acme), which has a
        // pending ManagerCheckIn review.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetUpcomingProbationEmployeeNamesAsync();

        Assert.True(
            names.Any(n => n.Contains("Carlos", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'Carlos Rivera' to appear in the upcoming probation reviews widget. " +
            $"Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task ClickingUpcomingProbationReviewItem_NavigatesToEmployeeProfile_WithProbationTabActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var employee  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.GetUpcomingProbationEmployeeNamesAsync();
        await dashboard.ClickFirstUpcomingProbationReviewAsync();

        Assert.Equal("Probation", await employee.GetActiveTabNameAsync());
    }

    // ── Sickness trio ─────────────────────────────────────────────────────────
    // These three widgets gate on Session.CanManageEmployees (HrAdministrator-only), which is
    // now redundant with the route guard (only an HrAdministrator can reach "/dashboard/hr" at
    // all), but is still asserted here directly for completeness.

    [Fact]
    public async Task HrAdministrator_Sees_SicknessTrioWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(OverdueReturnToWorkTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));

        await dashboard.WaitForWidgetLoadedAsync(CurrentSicknessAbsenceTitle);
        await dashboard.WaitForWidgetLoadedAsync(OverdueReturnToWorkTitle);
        await dashboard.WaitForWidgetLoadedAsync(MissingFitNotesTitle);
    }

    [Fact]
    public async Task ComplianceDocumentExpiryWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Document Compliance");
    }

    [Fact]
    public async Task RecentEmployeeChangesWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Recent Employee Changes");
    }
}
