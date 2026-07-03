using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the HR-only sickness dashboard widgets (Current Sickness Absence, Overdue
/// Return-to-Work Reviews, Missing Fit Notes) are shown for HR Administrator / Company
/// Administrator and hidden for a plain employee — these widgets gate on
/// Session.CanManageEmployees, which mirrors the sickness:manage policy roles
/// (HR Administrator, Company Administrator).
///
/// Also verifies the Team Sickness Today widget, which is visible to any authenticated
/// employee (it self-scopes to the caller's own direct reports; a plain employee with
/// no reports simply sees the empty state) — this matches the sickness:view-team policy
/// plus the endpoint's manager self-scoping check.
///
/// Uses seeded personas: Laura Bennett (HR Administrator), Tom Williams (plain Employee),
/// James Okafor (Employee + Manager).
/// </summary>
[Collection("E2E")]
public sealed class SicknessDashboardWidgetsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    private const string CurrentSicknessAbsenceTitle = "Current Sickness Absence";
    private const string OverdueReturnToWorkTitle    = "Overdue Return-to-Work Reviews";
    private const string MissingFitNotesTitle        = "Missing Fit Notes";
    private const string TeamSicknessTodayTitle      = "Team Sickness Today";

    [Fact]
    public async Task HrAdministrator_Sees_CurrentSicknessAbsenceWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle),
            "Expected the Current Sickness Absence widget to be visible for an HR Administrator");
    }

    [Fact]
    public async Task HrAdministrator_Sees_OverdueReturnToWorkReviewsWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(OverdueReturnToWorkTitle),
            "Expected the Overdue Return-to-Work Reviews widget to be visible for an HR Administrator");
    }

    [Fact]
    public async Task HrAdministrator_Sees_MissingFitNotesWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle),
            "Expected the Missing Fit Notes widget to be visible for an HR Administrator");
    }

    [Fact]
    public async Task PlainEmployee_DoesNotSee_CurrentSicknessAbsenceWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.False(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle),
            "Expected the Current Sickness Absence widget to be hidden for a plain employee");
    }

    [Fact]
    public async Task PlainEmployee_DoesNotSee_OverdueReturnToWorkReviewsWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.False(await dashboard.HasWidgetAsync(OverdueReturnToWorkTitle),
            "Expected the Overdue Return-to-Work Reviews widget to be hidden for a plain employee");
    }

    [Fact]
    public async Task PlainEmployee_DoesNotSee_MissingFitNotesWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.False(await dashboard.HasWidgetAsync(MissingFitNotesTitle),
            "Expected the Missing Fit Notes widget to be hidden for a plain employee");
    }

    [Fact]
    public async Task Manager_Sees_TeamSicknessTodayWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(TeamSicknessTodayTitle),
            "Expected the Team Sickness Today widget to be visible for a manager");

        await dashboard.WaitForWidgetLoadedAsync(TeamSicknessTodayTitle);
    }

    [Fact]
    public async Task PlainEmployee_Sees_TeamSicknessTodayWidget_SelfScoped_Empty()
    {
        // Team Sickness Today is visible to any authenticated employee — it self-scopes
        // to the caller's own direct reports. A plain employee with no direct reports
        // should see the widget with its empty state, not be denied entirely.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(TeamSicknessTodayTitle),
            "Expected the Team Sickness Today widget to be visible (self-scoped) for a plain employee");

        await dashboard.WaitForWidgetLoadedAsync(TeamSicknessTodayTitle);
    }
}
