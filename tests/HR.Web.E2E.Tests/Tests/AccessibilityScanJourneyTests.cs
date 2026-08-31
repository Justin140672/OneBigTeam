using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: applies the shared <see cref="AccessibilityScan"/> axe-core WCAG 2.0 A/AA gate across the
/// representative HR-administrator journeys — the post-login shell, employee administration (list
/// grid + edit page), leave configuration forms, a confirmation dialog, a data grid carrying status
/// badges, and the reporting catalogue plus two report pages. Dashboard journeys stay in
/// <see cref="AxeCoreDashboardScanTests"/>; employee self-service is in
/// <see cref="EmployeeSelfServiceAccessibilityScanTests"/>; unauthenticated auth is in
/// <see cref="LoginAccessibilityScanTests"/>.
/// </summary>
public sealed class AccessibilityScanJourneyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
    }

    [Fact]
    public async Task PostLoginShell_HasNoSeriousViolations()
    {
        await LoginAsync();
        await _page.GotoAsync(_fixture.WebBaseUrl);
        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "post-login application shell");
    }

    [Fact]
    public async Task EmployeeListGrid_HasNoSeriousViolations()
    {
        await LoginAsync();
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "employee list grid");
    }

    [Fact]
    public async Task EmployeeEditPage_HasNoSeriousViolations()
    {
        await LoginAsync();
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var edit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);
        await list.ClickEmployeeAsync("Tom Williams");
        await edit.OpenEmploymentTabAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "employee edit page (Employment tab)");
    }

    [Fact]
    public async Task LeaveTypesGrid_HasNoSeriousViolations_IncludingStatusBadges()
    {
        await LoginAsync();
        var leaveTypes = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        await leaveTypes.GoToAsync(AcmeId);
        // The leave types grid renders .status-badge / .status-badge--success severity indicators —
        // scanning it here keeps axe's colour-contrast rule covering that component (NFR-05 §6).

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "leave types grid (with status badges)");
    }

    [Fact]
    public async Task LeaveTypeEditForm_HasNoSeriousViolations()
    {
        await LoginAsync();
        var edit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "leave type create form");
    }

    [Fact]
    public async Task LeavePolicyEditForm_HasNoSeriousViolations()
    {
        await LoginAsync();
        var edit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "leave policy create form");
    }

    [Fact]
    public async Task HrConfirmDialog_Open_HasNoSeriousViolations()
    {
        await LoginAsync();
        var leaveTypes = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        await leaveTypes.GoToAsync(AcmeId);

        // Select the first data row and open the deactivate confirmation (HrConfirmDialog) — then
        // scan with the dialog visible. We deliberately do NOT confirm, so no state changes.
        await _page.Locator(".e-grid .e-row").First.ClickAsync();
        var deactivate = _page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await deactivate.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await deactivate.ClickAsync();
        await _page.GetByRole(AriaRole.Dialog)
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "HrConfirmDialog (leave type deactivate)");
    }

    [Fact]
    public async Task AssetsGrid_HasNoSeriousViolations()
    {
        await LoginAsync();
        var assets = new AssetListPage(_page, _fixture.WebBaseUrl);
        await assets.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "assets grid");
    }

    [Fact]
    public async Task ReportCatalogue_HasNoSeriousViolations()
    {
        await LoginAsync();
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        await catalog.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "reports catalogue");
    }

    [Fact]
    public async Task SicknessReport_HasNoSeriousViolations()
    {
        await LoginAsync();
        var report = new SicknessReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "sickness report");
    }

    [Fact]
    public async Task RecruitmentPipelineReport_HasNoSeriousViolations()
    {
        await LoginAsync();
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "recruitment pipeline report");
    }
}
