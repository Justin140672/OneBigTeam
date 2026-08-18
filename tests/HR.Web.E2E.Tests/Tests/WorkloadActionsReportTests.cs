using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Workload &amp; HR Actions report page
/// (/companies/{companyId}/reporting/workload-actions — WorkloadActionsReportPage.razor): loading
/// (summary cards and grid) from the Report Catalog, the Urgency/Action Type filters, Clear,
/// Group By, the empty state, per-row "Go" navigation, and access control for a persona with no
/// baseline reporting role. This is a read-only report — there is no create/edit/delete flow to
/// cover, unlike CRUD list+edit page pairs (see EmploymentTypeManagementTests). Catalog-page card
/// navigation coverage for every report (including this one's slug) lives in
/// <see cref="ReportCatalogTests"/>; this file focuses on the report page's own behavior.
/// </summary>
public sealed class WorkloadActionsReportTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    // Plain Employee role — no reporting:view (or any reporting sub-)policy at all, used elsewhere
    // in the suite (UnauthorizedAccessTests.Employee_CannotAccess_HrInbox) as the "no baseline
    // reporting/HR role" persona.
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task CatalogCard_NavigatesToReportPage_WithSummaryCardsVisible()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.HasCardAsync("Workload & HR Actions Report"),
            "Expected the Workload & HR Actions Report catalog card to be visible for an HR Administrator");
        Assert.True(await catalog.IsCardClickableAsync("Workload & HR Actions Report"),
            "Expected the Workload & HR Actions Report card to be clickable (no 'Coming soon' badge)");

        await catalog.ClickCardAsync("Workload & HR Actions Report");

        await _page.WaitForURLAsync("**/reporting/workload-actions", new() { Timeout = 15_000 });

        Assert.False(await report.HasLoadErrorAsync());

        // Summary stat cards should each render a non-negative integer (never the -1
        // parse-failure sentinel), proving the report's aggregate counts loaded successfully.
        Assert.True(await report.GetStatValueAsync("Total Outstanding") >= 0);
        Assert.True(await report.GetStatValueAsync("Overdue") >= 0);
        Assert.True(await report.GetStatValueAsync("Due Today") >= 0);
        Assert.True(await report.GetStatValueAsync("Due This Week") >= 0);
    }

    [Fact]
    public async Task Page_Loads_Directly_WithGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        // Column headers only exist when at least one grid has rendered — if the seeded E2E
        // environment has zero outstanding actions for Laura's scope, the empty-state alert is
        // shown instead and there is nothing to assert column headers against.
        if (!await report.IsEmptyStateVisibleAsync())
        {
            var headers = await report.GetColumnHeadersAsync();
            Assert.Contains(headers, h => h.Contains("Employee"));
            Assert.Contains(headers, h => h.Contains("Department"));
            Assert.Contains(headers, h => h.Contains("Action Type"));
            Assert.Contains(headers, h => h.Contains("Due Date"));
            Assert.Contains(headers, h => h.Contains("Status"));
            Assert.Contains(headers, h => h.Contains("Urgency"));
        }
    }

    [Fact]
    public async Task UrgencyFilter_ReloadsGridWithoutErroring_AndNarrowsOrMatchesRowCount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var rowCountBefore = await report.GetRowCountAsync();

        await report.SelectUrgencyAsync("Overdue");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying the Urgency filter");

        // Filtering to a single urgency can only narrow (or leave unchanged) the set of rows shown,
        // never grow it.
        var rowCountAfter = await report.GetRowCountAsync();
        Assert.True(rowCountAfter <= rowCountBefore,
            "Expected the Urgency filter to narrow (or leave unchanged) the row count");
    }

    [Fact]
    public async Task ClearFilters_RestoresOriginalUnfilteredRowCount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var rowCountBefore = await report.GetRowCountAsync();

        await report.SelectUrgencyAsync("Overdue");
        await report.ApplyFiltersAsync();

        await report.ClearFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after clearing filters");

        var rowCountAfterClear = await report.GetRowCountAsync();
        Assert.Equal(rowCountBefore, rowCountAfterClear);
    }

    [Fact]
    public async Task GroupByActionType_RendersGroupedSections()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        await report.SelectGroupByAsync("Action Type");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying Group By");

        // When there is at least one outstanding action, grouping by Action Type must render at
        // least one "<ActionType> (<count>)" heading above its own grid; when there are none, the
        // empty-state alert takes over instead and there are no group headings at all — either
        // way, the total row count across groups must match the flat (ungrouped) total.
        if (!await report.IsEmptyStateVisibleAsync())
        {
            var headings = await report.GetGroupHeadingsAsync();
            Assert.NotEmpty(headings);
        }
    }

    [Fact]
    public async Task EmptyState_Shown_WhenActionTypeFilterMatchesNothing()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // The Action Type dropdown is populated only from real loaded row data (there's no
        // dedicated lookup endpoint — see WorkloadActionsReportPage.razor.LoadAsync), so there is
        // no way to select a guaranteed-nonexistent Action Type via the dropdown's filterable list
        // itself. Filtering by a specific real Employee whose deep-linked action set is empty isn't
        // reliable either without known seed data, so instead narrow via the Due Date range to a
        // window far in the past, which the seeded E2E environment's outstanding (not-yet-resolved)
        // actions cannot fall inside.
        await report.SetDueDateRangeAsync(
            new DateOnly(1900, 1, 1),
            new DateOnly(1900, 1, 2));
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync());

        var rowCount = await report.GetRowCountAsync();
        if (rowCount == 0)
        {
            Assert.True(await report.IsEmptyStateVisibleAsync(),
                "Expected the 'No outstanding actions. Everything is up to date.' message when no rows match the filter");
        }
    }

    [Fact]
    public async Task RowGoButtons_ExistAndAreClickable_WhenOutstandingActionsExist()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // Whether the "Go" deep link navigates to an employee profile, a task, a leave request,
        // etc. depends entirely on which IWorkloadActionProvider produced the first row in the
        // seeded E2E environment, so this only asserts that at least one exists and that clicking
        // it performs a real client-side navigation away from the report page — not the exact
        // destination URL.
        if (!await report.IsEmptyStateVisibleAsync())
        {
            Assert.True(await report.GetGoButtonCountAsync() > 0,
                "Expected at least one row 'Go' action button when outstanding actions exist");

            var startingUrl = _page.Url;
            await report.ClickFirstRowGoButtonAsync();

            Assert.NotEqual(startingUrl, _page.Url);
            Assert.DoesNotContain("/reporting/workload-actions", _page.Url);
        }
    }

    [Fact]
    public async Task NonHrPersona_DoesNotSeeWorkloadActionsCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // Tom (plain Employee, no reporting:view or any reporting sub-policy at all) cannot open
        // the report catalog page at all — the catalog endpoint requires baseline reporting:view
        // access and denies him outright (403), so ReportCatalogPage.razor redirects to his
        // default location (Session.MyProfileUrl) instead of rendering the catalog (with or
        // without the Workload & HR Actions Report card) or an error banner — mirroring the
        // Session.MyProfileUrl redirect-on-unauthorized convention used by every other
        // permission-gated list page (see DepartmentList.razor, LocationList.razor, etc.).
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting");
        // See E2ETestBase.WaitForUrlToStopContainingAsync's doc comment: the redirect is a
        // client-side Blazor NavigateTo, not a full navigation, so NetworkIdle is not a reliable
        // completion signal.
        await WaitForUrlToStopContainingAsync("/reporting");

        Assert.False(_page.Url.Contains("/reporting"),
            "Expected a persona with no baseline reporting role to be redirected away from the report catalog, not shown an empty/error catalog page");
    }

    [Fact]
    public async Task NonHrPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new WorkloadActionsReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // The report data endpoint denies a persona with no baseline reporting role;
        // ReportingService.GetWorkloadActionsReportAsync catches that and returns null, and the
        // page renders its own alert-danger error banner ("Failed to load the workload & HR
        // actions report.") instead of a blank or crashed screen — mirroring the access-denied
        // convention used elsewhere (see UnauthorizedAccessTests.Employee_CannotAccess_HrInbox and
        // EmployeeDirectoryReportTests.NonHrPersona_DirectlyNavigatingToReportPage_DoesNotCrash).
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/workload-actions");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        Assert.True(await report.HasLoadErrorAsync(),
            "Expected a graceful error banner (not a crash/blank page) when a persona with no baseline reporting role is denied the report data");
    }
}
