using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers HR.Admin.Web's Operational Alerts surface (Follow-up B):
/// - /operational-alerts (OperationalAlerts.razor) — the platform-admin-only grid of
///   system-generated alerts, with debounced company-id + category + status filters.
/// - /operational-alerts/{id} (OperationalAlertDetails.razor) — full detail plus the
///   "Resolve alert" action (shared AdminActionConfirmDialog, min-5-char note,
///   POST .../operational-alerts/{id}/resolve).
///
/// Operational alerts are system-generated — there is no create UI — so the fixture's API host
/// seeds a deterministic pool via NotificationsModule.SeedE2eOperationalAlertsAsync (E2E_TESTING
/// only, mirroring the Employees E2E arrange-data pool): 8 open ReportGeneration alerts
/// (0000e2ea-…-00000000000N), one open Compliance alert (…-c001), and one already-resolved alert
/// (…-00000000f0) — all for the seeded Acme company. Each mutating test claims a distinct pool id
/// and is written to no-op cleanly if that alert was already resolved by a previous run against the
/// same long-lived fixture (same convention as BackgroundJobsAdminTests' retry-flow tests).
///
/// Auth follows the same allow-list pattern as CustomerListAdminTests / BackgroundJobsAdminTests:
/// "priya.shah@acme.example" is on "PlatformAdmin:AllowedEmails", "tom.williams@acme.example" is a
/// valid dev persona that is not — it is rejected on the Admin Portal login page itself.
/// </summary>
public sealed class OperationalAlertsManagementTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ResolvedAlertId = Guid.Parse("0000e2ea-0000-0000-0000-0000000000f0");
    private static readonly Guid ComplianceAlertId = Guid.Parse("0000e2ea-0000-0000-0000-00000000c001");
    private static Guid OpenPoolAlert(int n) => Guid.Parse($"0000e2ea-0000-0000-0000-0000000000{n:D2}");

    private async Task LoginAsAdminAsync()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);
    }

    [Fact]
    public async Task List_AllowListedAdmin_ShowsGridWithExpectedColumns()
    {
        await LoginAsAdminAsync();

        var list = new OperationalAlertsPage(_page, _fixture.AdminWebBaseUrl);
        await list.GotoAsync();

        Assert.False(await list.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the operational alerts grid, not the not-authorised banner");

        await list.SetCompanyIdFilterAsync(AcmeCompanyId);
        Assert.True(await list.IsGridVisibleAsync(), "Expected the seeded Acme alerts to render a grid");
        Assert.True(await list.RowCountAsync() > 0, "Expected at least one seeded open alert for Acme");

        foreach (var header in new[]
                 {
                     "Severity", "Category", "Company", "Summary", "Occurrences", "First seen", "Latest", "Status",
                 })
        {
            Assert.True(await list.HasColumnHeaderAsync(header), $"Expected grid column '{header}'");
        }
    }

    [Fact]
    public async Task NonAllowListedPersona_IsRejectedAtLogin_NotGivenAlertAccess()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        await login.GoToAsync();

        var error = await login.SubmitExpectingNotAuthorisedAsync(NonAllowListedEmail);

        Assert.Contains("not authorised", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(login.IsOnLoginPage(),
            "A non-allow-listed account must be rejected on the login page, not handed a session");
    }

    [Fact]
    public async Task AnonymousAccess_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/operational-alerts");
        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Filtering_ByCategoryAndStatus_ChangesResultSet()
    {
        await LoginAsAdminAsync();

        var list = new OperationalAlertsPage(_page, _fixture.AdminWebBaseUrl);
        await list.GotoAsync();
        await list.SetCompanyIdFilterAsync(AcmeCompanyId);

        // Status defaults to "open". Filter to the Compliance category — every visible row must be
        // Compliance, and the one seeded open Compliance alert must be present.
        await list.SetCategoryFilterAsync("Compliance");
        var complianceCategories = await list.ColumnValuesAsync("Category");
        Assert.NotEmpty(complianceCategories);
        Assert.All(complianceCategories, c => Assert.Equal("Compliance", c));

        // Switch to ReportGeneration — a different, non-empty result set (8 seeded open alerts).
        await list.SetCategoryFilterAsync("ReportGeneration");
        var reportCategories = await list.ColumnValuesAsync("Category");
        Assert.NotEmpty(reportCategories);
        Assert.All(reportCategories, c => Assert.Equal("ReportGeneration", c));

        // status=resolved (all categories) surfaces the seeded resolved alert and only resolved rows.
        await list.SetCategoryFilterAsync("");
        await list.SetStatusFilterAsync("resolved");
        var resolvedStatuses = await list.ColumnValuesAsync("Status");
        Assert.NotEmpty(resolvedStatuses);
        Assert.All(resolvedStatuses, s => Assert.Equal("Resolved", s));

        // status=open must not contain any resolved rows.
        await list.SetStatusFilterAsync("open");
        var openStatuses = await list.ColumnValuesAsync("Status");
        Assert.NotEmpty(openStatuses);
        Assert.All(openStatuses, s => Assert.Equal("Open", s));
    }

    [Fact]
    public async Task OpenAlertFromList_ShowsDetailFields()
    {
        await LoginAsAdminAsync();

        var list = new OperationalAlertsPage(_page, _fixture.AdminWebBaseUrl);
        await list.GotoAsync();
        await list.SetCompanyIdFilterAsync(AcmeCompanyId);
        await list.SetCategoryFilterAsync("Compliance");

        await list.OpenFirstRowAsync();

        var details = new OperationalAlertDetailsPage(_page, _fixture.AdminWebBaseUrl);
        Assert.False(await details.IsErrorBannerVisibleAsync());
        Assert.Equal("Compliance", await details.CategoryAsync());
        Assert.Equal("Open", await details.StatusAsync());
        Assert.Equal(AcmeCompanyId.ToString(), await details.CompanyIdAsync());
        Assert.False(string.IsNullOrWhiteSpace(await details.SeverityAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await details.SummaryAsync()));
    }

    [Fact]
    public async Task ResolveOpenAlert_ShowsSuccess_AndRemovesResolveButton()
    {
        await LoginAsAdminAsync();

        var details = new OperationalAlertDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var alertId = OpenPoolAlert(1);
        await details.GotoAsync(alertId);

        if (!await details.ResolveButtonVisibleAsync())
        {
            // Already resolved by a previous run against this long-lived fixture — the resolve path
            // is not reachable from the UI. Assert the terminal state instead of depending on
            // unseeded state (same convention as BackgroundJobsAdminTests).
            Assert.Equal("Resolved", await details.StatusAsync());
            return;
        }

        await details.ResolveAsync("Root cause fixed — re-ran the report generation job.");

        Assert.True(await details.SuccessVisibleAsync(), "Expected the .admin-action-success message after resolving");
        Assert.Equal("Resolved", await details.StatusAsync());
        Assert.False(await details.ResolveButtonVisibleAsync(),
            "The Resolve button must disappear once the alert is Resolved");

        // Re-navigating to the now-resolved alert offers no Resolve action.
        await details.GotoAsync(alertId);
        Assert.False(await details.ResolveButtonVisibleAsync());
        Assert.Equal("Resolved", await details.StatusAsync());
    }

    [Fact]
    public async Task ResolveDialog_RejectsTooShortNote()
    {
        await LoginAsAdminAsync();

        var details = new OperationalAlertDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var alertId = OpenPoolAlert(2);
        await details.GotoAsync(alertId);

        if (!await details.ResolveButtonVisibleAsync())
        {
            Assert.Equal("Resolved", await details.StatusAsync());
            return;
        }

        await details.OpenResolveDialogAsync();

        // Empty note -> min-length guard, dialog stays open, no result message.
        await details.ClickResolveConfirmAsync();
        var validation = await details.DialogValidationErrorAsync() ?? "";
        Assert.Contains("reason", validation, StringComparison.OrdinalIgnoreCase);
        Assert.True(await details.ResolveDialogVisibleAsync(), "Dialog should stay open with no note entered");
        Assert.False(await details.SuccessVisibleAsync());
    }

    [Fact]
    public async Task PreSeededResolvedAlert_HasNoResolveButton()
    {
        await LoginAsAdminAsync();

        var details = new OperationalAlertDetailsPage(_page, _fixture.AdminWebBaseUrl);
        await details.GotoAsync(ResolvedAlertId);

        Assert.False(await details.IsErrorBannerVisibleAsync());
        Assert.Equal("Resolved", await details.StatusAsync());
        Assert.False(await details.ResolveButtonVisibleAsync(),
            "A resolved alert must not offer the Resolve action");
        Assert.False(string.IsNullOrWhiteSpace(await details.ResolutionNoteAsync()));
    }
}
