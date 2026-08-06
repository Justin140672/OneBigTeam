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
/// ("Document Compliance"), DocumentReviewsWidget ("Document Reviews"), and
/// RecentEmployeeChangesWidget.
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
        Assert.True(await dashboard.HasWidgetAsync("Gender Split"));
        Assert.True(await dashboard.HasWidgetAsync("Employment Type"));
        Assert.True(await dashboard.HasWidgetAsync("HR Inbox"));
        Assert.True(await dashboard.HasWidgetAsync("Leave Requests"));
        Assert.True(await dashboard.HasWidgetAsync("Upcoming Probation Reviews"));
        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(OverdueReturnToWorkTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));
        Assert.True(await dashboard.HasWidgetAsync("Document Compliance"));
        Assert.True(await dashboard.HasWidgetAsync("Document Reviews"));
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

    // ── Gender Split / Employment Type Split Charts ──────────────────────────
    // GenderSplitChart.razor and EmploymentTypeSplitChart.razor are non-interactive
    // SfAccumulationChart doughnuts, gated on Session.CanManageEmployees (redundant with the
    // route guard, same as the sickness trio). The seeded Acme company (Laura's company) has
    // active employees with genders and employment types set, so these tests exercise the
    // populated-chart path; there is no seeded company with zero active employees available to
    // this suite to exercise the "No employee data available." empty state end-to-end without
    // provisioning a brand-new company, which is outside this suite's existing patterns.

    [Fact]
    public async Task GenderSplitChart_Loads()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Gender Split"));

        await dashboard.WaitForGenderSplitChartLoadedAsync();

        // Acme has active employees with genders set, so this should render the chart, not the
        // empty state.
        Assert.False(await dashboard.GenderSplitChartIsEmptyAsync());
    }

    [Fact]
    public async Task EmploymentTypeSplitChart_Loads()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Employment Type"));

        await dashboard.WaitForEmploymentTypeSplitChartLoadedAsync();

        // Acme has active employees with employment types set, so this should render the chart,
        // not the empty state.
        Assert.False(await dashboard.EmploymentTypeSplitChartIsEmptyAsync());
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
    public async Task ClickingUpcomingProbationReviewItem_OpensReviewTaskDialog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetUpcomingProbationEmployeeNamesAsync();
        await dashboard.ClickFirstUpcomingProbationReviewAsync();

        // GenerateDueProbationReviewsJob always creates a task for each seeded review, so
        // UpcomingProbationReviewsWidget.OnReviewClicked opens the task dialog in place rather
        // than navigating away (see ClickFirstUpcomingProbationReviewAsync).
        await task.WaitForLoadedAsync();
        Assert.Contains("/dashboard/hr", _page.Url);

        var title = await task.GetTitleAsync();
        Assert.Contains(names[0], title, StringComparison.OrdinalIgnoreCase);
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

    // ── Document Reviews Widget ───────────────────────────────────────────────
    // DocumentReviewsWidget.razor ("Document Reviews") surfaces SharedCompanyDocuments whose
    // ReviewDate falls within the next 7 days, split into "overdue" (ReviewDate < today) and "due
    // this week" buckets — see ListSharedCompanyDocumentsDueForReviewHandler. Documents are
    // uploaded here via the same Shared Documents list page flow as SharedDocumentUploadTests /
    // SharedDocumentReviewFrequencyTests; a Next Review Date can be set directly on upload without
    // picking a Review Frequency first (the dialog only requires the reverse — a frequency other
    // than "None" requires a date, not the other way round), so no Review Frequency selection is
    // needed to get a document into this widget's window.

    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task DocumentReviewsWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Document Reviews");
    }

    [Fact]
    public async Task DocumentReviewsWidget_ShowsOverdueAndDueThisWeekDocuments_AndNavigatesToDetailOnClick()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var overdueTitle     = $"Overdue Policy {Guid.NewGuid():N}";
        var dueThisWeekTitle = $"Due Soon Policy {Guid.NewGuid():N}";
        var overdueFile      = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var dueThisWeekFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // ReviewDate in the past — the handler's IsOverdue = ReviewDate < today marks this one
            // "overdue".
            await UploadDocumentWithReviewDateAsync(
                overdueTitle, overdueFile, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)));

            // ReviewDate a few days out — still inside the widget's "today + 7 days" window, so it
            // lands in the "due this week" bucket instead.
            await UploadDocumentWithReviewDateAsync(
                dueThisWeekTitle, dueThisWeekFile, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));

            await dashboard.GoToAsync();
            var titles = await dashboard.GetDocumentReviewTitlesAsync();

            Assert.Contains(titles, t => t.Contains(overdueTitle, StringComparison.Ordinal));
            Assert.Contains(titles, t => t.Contains(dueThisWeekTitle, StringComparison.Ordinal));

            await dashboard.ClickDocumentReviewItemAsync(overdueTitle);

            Assert.Contains($"/companies/{AcmeId}/shared-documents/", _page.Url);
            await Assertions.Expect(_page.Locator("h1")).ToContainTextAsync(overdueTitle, new() { Timeout = 10_000 });
        }
        finally
        {
            if (File.Exists(overdueFile)) File.Delete(overdueFile);
            if (File.Exists(dueThisWeekFile)) File.Delete(dueThisWeekFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page with a specific Next Review
    // Date, leaving Review Frequency at its "None" default — same upload-dialog interaction
    // pattern as SharedDocumentUploadTests.HrAdministrator_CanUploadSharedDocument_AndSeeItInList /
    // SharedDocumentReviewFrequencyTests.UploadDocumentAsync, but setting the date directly instead
    // of the fixed one-year-out date those use for their own (unrelated) assertions.
    private async Task UploadDocumentWithReviewDateAsync(string title, string filePath, DateOnly reviewDate)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        var reviewDateInput = dialog.Locator(".col-md-6")
            .Filter(new() { HasText = "Next Review Date" })
            .Locator(".e-date-wrapper input.e-input");
        await reviewDateInput.ClickAsync();
        await reviewDateInput.FillAsync(reviewDate.ToString("dd/MM/yyyy"));
        await _page.Keyboard.PressAsync("Tab");

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
