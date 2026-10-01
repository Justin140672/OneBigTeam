using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

[Collection("ReportFavourites")]
public sealed class HrDashboardTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    private const string CurrentSicknessAbsenceTitle = "Current Sickness Absence";
    private const string MissingFitNotesTitle        = "Missing Fit Notes";

    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task NonHrAdministrator_IsRedirectedAway_FromHrDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/hr");

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
        Assert.True(await dashboard.HasWidgetAsync("Needs your action"));
        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));
        Assert.True(await dashboard.HasWidgetAsync("Recent Employee Changes"));
        Assert.True(await dashboard.HasWidgetAsync("Favourite Reports"));
    }


    [Fact]
    public async Task AttentionQueueSection_PrecedesAnalyticsSection_InDom()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();
        await dashboard.WaitForHeadcountChartLoadedAsync();

        var queueY = (await _page.Locator(".attention-queue-card").First.BoundingBoxAsync())?.Y;
        var analyticsY = (await _page.Locator(".dashboard-analytics-grid").First.BoundingBoxAsync())?.Y;

        Assert.NotNull(queueY);
        Assert.NotNull(analyticsY);
        Assert.True(queueY < analyticsY,
            $"Expected the attention queue (y={queueY}) to render above the analytics grid (y={analyticsY}).");
    }


    [Fact]
    public async Task WaitingOnOthers_ShowsCarlosRivera_ProbationReview_OwnedByManager()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var carlosRow = _page.GetByTestId("waiting-on-others").Locator(".waiting-item").Filter(new() { HasText = "Carlos" });
        await Assertions.Expect(carlosRow.First).ToBeVisibleAsync();
        await Assertions.Expect(carlosRow.First.Locator(".waiting-owner")).ToContainTextAsync("Responsible:");
    }

    [Fact]
    public async Task ClickingAttentionQueueTaskBackedItem_OpensTaskDialog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickFirstTaskBackedAttentionQueueItemAsync();

        await task.WaitForLoadedAsync();
        Assert.Contains("/dashboard/hr", _page.Url);
        Assert.False(string.IsNullOrWhiteSpace(await task.GetTitleAsync()));
    }

    [Fact]
    public async Task AttentionQueue_ItemsShowSubjectCategoryAndActionLabel()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();

        var firstRow = dashboard.ActionableAttentionQueueRows.First;
        if (!await firstRow.IsVisibleAsync())
        {
            return;
        }

        await Assertions.Expect(firstRow.Locator(".task-widget-title")).ToBeVisibleAsync();
        await Assertions.Expect(firstRow.Locator(".task-widget-meta")).ToBeVisibleAsync();
        await Assertions.Expect(firstRow.Locator(".attention-queue-action")).ToBeVisibleAsync();

        var ariaLabel = await firstRow.GetAttributeAsync("aria-label");
        Assert.False(string.IsNullOrWhiteSpace(ariaLabel));
    }

    [Fact]
    public async Task AttentionQueue_OrdersOverdueItemsBeforeNonOverdueItems()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();

        var overdueFlags = await dashboard.GetAttentionQueueOverdueFlagsAsync();
        if (overdueFlags.Count == 0)
        {
            return;
        }

        var seenNonOverdue = false;
        for (var i = 0; i < overdueFlags.Count; i++)
        {
            var isOverdue = overdueFlags[i];

            if (!isOverdue)
                seenNonOverdue = true;
            else
                Assert.False(seenNonOverdue,
                    $"Row {i} is overdue but appears after a non-overdue row — overdue items must sort first.");
        }
    }


    [Fact]
    public async Task AttentionQueue_ShowsAllClearSummary_WhenEmpty()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();

        var subjects = await dashboard.GetAttentionQueueSubjectsAsync();
        var isAllClear = await dashboard.AttentionQueueIsAllClearAsync();

        Assert.Equal(subjects.Count == 0, isAllClear);

        if (isAllClear)
        {
            await Assertions.Expect(_page.Locator(".attention-queue-all-clear")).ToContainTextAsync("All clear");
        }
    }

    [Fact]
    public async Task DocumentReviewRow_ShowsOverdueAndDueThisWeekDocuments_AndNavigatesToDetailOnClick()
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
            await UploadDocumentWithReviewDateAsync(
                overdueTitle, overdueFile, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)));

            await UploadDocumentWithReviewDateAsync(
                dueThisWeekTitle, dueThisWeekFile, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));

            await dashboard.GoToAsync();
            var subjects = await dashboard.GetAttentionQueueSubjectsAsync();

            Assert.Contains(subjects, t => t.Contains(overdueTitle, StringComparison.Ordinal));
            Assert.Contains(subjects, t => t.Contains(dueThisWeekTitle, StringComparison.Ordinal));

            Assert.True(
                subjects.ToList().IndexOf(subjects.First(t => t.Contains(overdueTitle, StringComparison.Ordinal)))
                < subjects.ToList().IndexOf(subjects.First(t => t.Contains(dueThisWeekTitle, StringComparison.Ordinal))),
                "Expected the overdue document review to sort ahead of the due-this-week one.");

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
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }


    [Fact]
    public async Task AnalyticsGrid_RendersAllThreeChartsTogether_AtDesktopViewport()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await _page.SetViewportSizeAsync(1440, 900);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();
        await dashboard.WaitForGenderSplitChartLoadedAsync();
        await dashboard.WaitForEmploymentTypeSplitChartLoadedAsync();

        Assert.True(await dashboard.HasWidgetAsync("Headcount by Department"));
        Assert.True(await dashboard.HasWidgetAsync("Gender Split"));
        Assert.True(await dashboard.HasWidgetAsync("Employment Type"));

        var bounds = await dashboard.GetAnalyticsGridTileBoundsAsync();
        Assert.Equal(3, bounds.Count);

        var ys = bounds.Select(b => b.Y).ToList();
        var maxYDelta = ys.Max() - ys.Min();
        Assert.True(maxYDelta < 40,
            $"Expected the three analytics tiles to sit in the same row at desktop width; y positions were [{string.Join(", ", ys)}].");
    }

    [Fact]
    public async Task HeadcountByDepartmentChart_Loads_AndShowsPlainTextLabels()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();

        var labels = await dashboard.GetHeadcountDepartmentLabelsAsync();
        Assert.NotEmpty(labels);
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    [Fact]
    public async Task HeadcountByDepartmentChart_ViewAllEmployees_HasDescriptiveAccessibleName_AndNavigates()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();

        await dashboard.ClickHeadcountViewAllEmployeesAsync();

        Assert.Contains($"/companies/{AcmeId}/employees", _page.Url);
    }

    [Fact]
    public async Task GenderSplitChart_Loads_AndShowsPlainTextLabelsWithoutHover()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Gender Split"));

        await dashboard.WaitForGenderSplitChartLoadedAsync();

        Assert.False(await dashboard.GenderSplitChartIsEmptyAsync());

        var labels = await dashboard.GetGenderSplitLabelsAsync();
        Assert.NotEmpty(labels);
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    [Fact]
    public async Task EmploymentTypeSplitChart_Loads_AndShowsPlainTextLabelsWithoutHover()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Employment Type"));

        await dashboard.WaitForEmploymentTypeSplitChartLoadedAsync();

        Assert.False(await dashboard.EmploymentTypeSplitChartIsEmptyAsync());

        var labels = await dashboard.GetEmploymentTypeSplitLabelsAsync();
        Assert.NotEmpty(labels);
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }


    [Fact]
    public async Task ViewAllLinks_HaveDescriptiveAccessibleNames_NotGenericViewAll()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();

        var headcountLink = _page.GetByRole(AriaRole.Link, new() { Name = "View all employees", Exact = true });
        await Assertions.Expect(headcountLink).ToBeVisibleAsync();

        var genericViewAll = _page.GetByRole(AriaRole.Link, new() { Name = "View all", Exact = true });
        Assert.Equal(0, await genericViewAll.CountAsync());
    }

    [Fact]
    public async Task KeyboardNavigation_CanTabToAttentionQueueItem_AndActivateWithEnter()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();

        // Actionable (button) rows only — read-only/stale rows are deliberately non-focusable
        // divs (see HrDashboardPage.ActionableAttentionQueueRows).
        var firstItem = dashboard.ActionableAttentionQueueRows.First;
        if (!await firstItem.IsVisibleAsync())
        {
            return;
        }

        await firstItem.FocusAsync();

        var isFocused = await firstItem.EvaluateAsync<bool>("el => el === document.activeElement");
        Assert.True(isFocused, "Expected the first attention-queue row to be keyboard-focusable.");

        var outlineStyle = await firstItem.EvaluateAsync<string>(
            "el => getComputedStyle(el).outlineStyle");
        Assert.NotEqual("none", outlineStyle);
    }

    [Fact]
    public async Task KeyboardNavigation_CanTabToViewAllEmployeesLink_AndFocusIsVisible()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();

        var link = _page.GetByRole(AriaRole.Link, new() { Name = "View all employees", Exact = true });
        await link.FocusAsync();

        var isFocused = await link.EvaluateAsync<bool>("el => el === document.activeElement");
        Assert.True(isFocused, "Expected the 'View all employees' link to be keyboard-focusable.");
    }


    [Theory]
    [InlineData(1440, 900)]
    [InlineData(834, 1112)]
    [InlineData(390, 844)]
    public async Task Dashboard_LoadsAndKeepsQueueAndAnalyticsUsable_AtViewport(int width, int height)
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await _page.SetViewportSizeAsync(width, height);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();
        await dashboard.WaitForHeadcountChartLoadedAsync();

        await Assertions.Expect(_page.Locator(".attention-queue-card").First).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".dashboard-analytics-grid").First).ToBeVisibleAsync();
        Assert.True(await dashboard.HasWidgetAsync("Headcount by Department"));
    }


    [Fact]
    public async Task HrAdministrator_Sees_RemainingSicknessWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));

        await dashboard.WaitForWidgetLoadedAsync(CurrentSicknessAbsenceTitle);
        await dashboard.WaitForWidgetLoadedAsync(MissingFitNotesTitle);
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


    [Fact]
    public async Task FavouriteReportsWidget_ShowsEmptyState_WhenNothingFavourited()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var titles = await dashboard.GetFavouriteReportTitlesAsync();
        Assert.Empty(titles);
    }

    [Fact]
    public async Task FavouriteReportsWidget_ShowsFavouritedReport_AndNavigatesToItOnClick()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog   = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);
        if (await catalog.IsFavouritedAsync("Employee Starter Report"))
            await catalog.ClickFavouriteAsync("Employee Starter Report");
        Assert.False(await catalog.IsFavouritedAsync("Employee Starter Report"));
        await catalog.ClickFavouriteAsync("Employee Starter Report");
        Assert.True(await catalog.IsFavouritedAsync("Employee Starter Report"));

        try
        {
            await dashboard.GoToAsync();

            var titles = await dashboard.GetFavouriteReportTitlesAsync();
            Assert.Contains(titles, t => t.Contains("Employee Starter Report", StringComparison.Ordinal));

            await dashboard.ClickFavouriteReportItemAsync("Employee Starter Report");

            await _page.WaitForURLAsync("**/reporting/employee-starters", new() { Timeout = 15_000 });
        }
        finally
        {
            await catalog.GoToAsync(AcmeId);
            if (await catalog.IsFavouritedAsync("Employee Starter Report"))
                await catalog.ClickFavouriteAsync("Employee Starter Report");
        }
    }

    [Fact]
    public async Task FavouriteReportsWidget_BrowseAll_NavigatesToReportCatalog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickFavouriteReportsBrowseAllAsync();

        Assert.Contains($"/companies/{AcmeId}/reporting", _page.Url);
    }
}
