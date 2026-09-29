using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class WorkloadActionsReportTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

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

        if (!await report.IsEmptyStateVisibleAsync())
        {
            Assert.True(await report.GetGoButtonCountAsync() > 0,
                "Expected at least one row 'Go' action button when outstanding actions exist");

            var startingUrl = _page.Url;
            await report.ClickFirstRowGoButtonAsync();

            var taskDialog = _page.GetByRole(AriaRole.Dialog).Filter(new() { Has = _page.Locator("[data-testid='task-title']") });
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!await taskDialog.IsVisibleAsync() && _page.Url == startingUrl && DateTime.UtcNow < deadline)
            {
                await _page.WaitForTimeoutAsync(200);
            }

            if (await taskDialog.IsVisibleAsync())
            {
                await Assertions.Expect(taskDialog).ToBeVisibleAsync();
            }
            else
            {
                Assert.NotEqual(startingUrl, _page.Url);
                Assert.DoesNotContain("/reporting/workload-actions", _page.Url);
            }
        }
    }

    [Fact]
    public async Task NonHrPersona_DoesNotSeeWorkloadActionsCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting");
        await WaitForUrlToStopContainingAsync("/reporting");

        Assert.False(_page.Url.Contains("/reporting"),
            "Expected a persona with no baseline reporting role to be redirected away from the report catalog, not shown an empty/error catalog page");
    }

    [Fact]
    public async Task NonHrPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/workload-actions");

        await accessDenied.WaitForLoadedAsync();
        Assert.True(accessDenied.IsOnRoute, $"Expected redirect to /access-denied, was: {_page.Url}");
    }
}
