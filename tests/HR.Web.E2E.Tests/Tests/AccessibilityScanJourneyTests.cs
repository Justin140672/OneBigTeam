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

        // Select a non-system leave type and open the deactivate confirmation (HrConfirmDialog) —
        // then scan with the dialog visible. We deliberately do NOT confirm, so no state changes.
        // The first row is "Annual Leave", a system type whose Deactivate action short-circuits to
        // an inline error and never opens the dialog (see LeaveTypeList.ConfigureToolbar), so target
        // "Unpaid Leave" explicitly instead.
        await _page.Locator(".e-grid .e-row").Filter(new() { HasText = "Unpaid Leave" }).First.ClickAsync();
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
}

/// <summary>
/// NFR-05: the Recruitment Pipeline report is gated by the Recruiter-only
/// "reporting:view-recruitment" policy (deliberately non-overlapping with HrAdministrator — see
/// <see cref="RecruitmentPipelineReportTests"/>), so its accessibility journey runs under the
/// Recruiter persona rather than the HR-administrator fixture the rest of
/// <see cref="AccessibilityScanJourneyTests"/> uses.
/// </summary>
public sealed class RecruitmentAccessibilityScanJourneyTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter

    [Fact]
    public async Task RecruitmentPipelineReport_HasNoSeriousViolations()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "recruitment pipeline report");
    }
}

/// <summary>
/// NFR-05: the Company edit page's Profile tab (registered office + trading address fields) is
/// gated by the "company:manage" policy, which CompanyAdministrator holds but HrAdministrator no
/// longer does (see <see cref="Tests.CompanyEditCloseBehaviorTests"/>) — so this journey runs
/// under the CompanyAdministrator-only persona rather than the HR-administrator fixture the rest
/// of <see cref="AccessibilityScanJourneyTests"/> uses. Added alongside the persistent visible
/// address field labels (Address Line 1/2, Town/City, County/Region, Postcode) that replaced
/// placeholder-only labelling.
/// </summary>
public sealed class CompanyProfileAccessibilityScanJourneyTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string CompanyAdminEmail = "priya.shah@acme.example"; // CompanyAdministrator

    [Fact]
    public async Task CompanyProfileTab_AddressFields_HasNoSeriousViolations()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        // Acme has both a Registered Office and a Trading Address seeded, so both address
        // sections (and their persistent field labels) are visible on the tab in one scan.
        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "company profile — address fields");
    }
}
