using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the HR-only dashboard (src/HR.Web/Components/Pages/Dashboards/HrDashboard.razor),
/// reached via "/dashboard/hr". The page guards on Session.IsHrAdministrator and redirects any
/// other role to Session.MyProfileUrl, so a non-HR-administrator can never see any of the
/// widgets below at all.
///
/// Layout (top to bottom): greeting header -> DashboardSwitcher -> "Needs your attention" row
/// (AttentionQueueWidget, a unified priority-sorted queue that replaced the standalone
/// HrInboxWidget, LeaveRequestsWidget, UpcomingProbationReviewsWidget,
/// OverdueReturnToWorkReviewsWidget, ComplianceDocumentExpiryWidget and DocumentReviewsWidget —
/// alongside FavouriteReportsWidget in the same row, item 46) -> an "Analytics" section with a
/// 3-chart grid (HeadcountByDepartmentChart, GenderSplitChart, EmploymentTypeSplitChart — all now
/// plain horizontal-bar charts, not Syncfusion donuts) -> a "More" section (CurrentSicknessAbsenceWidget,
/// MissingFitNotesWidget, RecentEmployeeChangesWidget).
///
/// Uses seeded personas: Laura Bennett (HR Administrator only) and Tom Williams (plain Employee).
///
/// Runs serialized against ReportCatalogTests (HrFavouritesSerialTestBase) — both toggle Laura
/// Bennett's shared, server-persisted report favourites, and the FavouriteReportsWidget tests
/// below assert on her favourites being empty/exactly-one, which races ReportCatalogTests'
/// equivalent favourite toggles under real concurrency. See GroupSerializedTestBases.cs.
/// </summary>
public sealed class HrDashboardTests(HrAdminPersonaFixture fixture) : HrFavouritesSerialTestBase(fixture)
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
        Assert.True(await dashboard.HasWidgetAsync("Needs your attention"));
        Assert.True(await dashboard.HasWidgetAsync(CurrentSicknessAbsenceTitle));
        Assert.True(await dashboard.HasWidgetAsync(MissingFitNotesTitle));
        Assert.True(await dashboard.HasWidgetAsync("Recent Employee Changes"));
        Assert.True(await dashboard.HasWidgetAsync("Favourite Reports"));
    }

    // ── Ordering: attention queue precedes analytics ──────────────────────────

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

    // ── AttentionQueueWidget ("Needs your attention") ─────────────────────────
    // Merges HR tasks, pending leave requests, overdue probation/return-to-work reviews, document
    // expiry and document reviews into a single priority-sorted list — see
    // src/HR.Web/Components/Pages/Dashboards/AttentionQueueWidget.razor.

    [Fact]
    public async Task AttentionQueue_ShowsCarlosRivera_ProbationReview()
    {
        // Depends on the seeded "Carlos Rivera" probation record (company: Acme), which has a
        // pending ManagerCheckIn review — previously covered by the standalone
        // UpcomingProbationReviewsWidget.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        // Gate against ProbationReviewFlowTests.CompletingReviewTask_IsReflectedOnProbationTab,
        // which completes Sophie Laurent's seeded pending review — while pending, her review can
        // sort ahead of and evict Carlos Rivera's from the queue's ordering. See
        // SharedProbationGate's remarks in GroupSerializedTestBases.cs.
        await SharedProbationGate.Instance.WaitAsync();
        try
        {
            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);
            await dashboard.GoToAsync();

            var subjects = await dashboard.GetAttentionQueueSubjectsAsync();

            Assert.True(
                subjects.Any(n => n.Contains("Carlos", StringComparison.OrdinalIgnoreCase)),
                $"Expected 'Carlos Rivera' to appear in the attention queue. " +
                $"Subjects found: [{string.Join(", ", subjects)}]");
        }
        finally
        {
            SharedProbationGate.Instance.Release();
        }
    }

    [Fact]
    public async Task ClickingAttentionQueueProbationReviewItem_OpensReviewTaskDialog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await SharedProbationGate.Instance.WaitAsync();
        try
        {
            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);
            await dashboard.GoToAsync();

            var subjects = await dashboard.GetAttentionQueueSubjectsAsync();
            var carlos = subjects.First(n => n.Contains("Carlos", StringComparison.OrdinalIgnoreCase));
            await dashboard.ClickAttentionQueueItemAsync(carlos);

            // GenerateDueProbationReviewsJob always creates a task for each seeded review, so the
            // queue's activation opens the task dialog in place rather than navigating away.
            await task.WaitForLoadedAsync();
            Assert.Contains("/dashboard/hr", _page.Url);

            var title = await task.GetTitleAsync();
            Assert.Contains(carlos, title, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SharedProbationGate.Instance.Release();
        }
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

        // Rather than assert on any single seeded item (which can rotate as other suites' data
        // changes), assert on the structural contract every row makes: a subject
        // (.task-widget-title), a category/status line (.task-widget-meta), and a primary action
        // control (.attention-queue-action) — see AttentionQueueWidget.razor's row markup.
        var firstRow = _page.Locator(".attention-queue-item").First;
        if (!await firstRow.IsVisibleAsync())
        {
            // No exceptions currently seeded for this run — covered separately by the "all clear"
            // test below.
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

        var rows = _page.Locator(".attention-queue-item");
        var count = await rows.CountAsync();
        if (count == 0)
        {
            // Empty queue for this run — ordering has nothing to assert; covered by the
            // structural test above and the "all clear" test below.
            return;
        }

        // AttentionQueueWidget orders overdue-first (OrderByDescending(i => i.IsOverdue)), so once
        // a row's class stops carrying "attention-queue-item--overdue", no later row should have
        // it either.
        var seenNonOverdue = false;
        for (var i = 0; i < count; i++)
        {
            var classes = await rows.Nth(i).GetAttributeAsync("class") ?? "";
            var isOverdue = classes.Contains("attention-queue-item--overdue");

            if (!isOverdue)
                seenNonOverdue = true;
            else
                Assert.False(seenNonOverdue,
                    $"Row {i} is overdue but appears after a non-overdue row — overdue items must sort first.");
        }
    }

    [Fact]
    public async Task AttentionQueue_HidesResolvedLeaveRequestsByDefault_AndRevealsThemViaToggle()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForAttentionQueueLoadedAsync();

        if (!await dashboard.HasShowResolvedLeaveToggleAsync())
        {
            // No resolved (Approved/Declined/Rejected) leave requests seeded for Acme in this
            // run — AttentionQueueWidget only renders the toggle when _resolvedLeaveCount > 0.
            // Noting as a gap rather than fabricating a resolved leave request via the UI, which
            // would require driving a full leave-request-and-decision flow just for this test.
            return;
        }

        var subjectsBefore = await dashboard.GetAttentionQueueSubjectsAsync();
        var countBefore = subjectsBefore.Count;

        await dashboard.SetShowResolvedLeaveRequestsAsync(true);
        var subjectsAfter = await dashboard.GetAttentionQueueSubjectsAsync();

        Assert.True(subjectsAfter.Count > countBefore,
            "Expected enabling 'Show resolved leave requests' to reveal at least one additional row.");

        await dashboard.SetShowResolvedLeaveRequestsAsync(false);
        var subjectsRestored = await dashboard.GetAttentionQueueSubjectsAsync();
        Assert.Equal(countBefore, subjectsRestored.Count);
    }

    [Fact]
    public async Task AttentionQueue_ShowsAllClearSummary_WhenEmpty()
    {
        // GAP: there is no seeded company reachable by this suite's fixtures with a genuinely
        // empty attention queue (Acme always has some mix of seeded HR tasks / leave requests /
        // reviews / documents). This test asserts the contract defensively: whenever the queue
        // happens to be empty, the compact "All clear" summary must be shown instead of any
        // individual empty-state cards, and vice versa — never both, never neither.
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
            // ReviewDate in the past — the handler's IsOverdue = ReviewDate < today marks this one
            // "overdue".
            await UploadDocumentWithReviewDateAsync(
                overdueTitle, overdueFile, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)));

            // ReviewDate a few days out — still inside the widget's "today + 7 days" window, so it
            // lands in the "due this week" bucket instead.
            await UploadDocumentWithReviewDateAsync(
                dueThisWeekTitle, dueThisWeekFile, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));

            await dashboard.GoToAsync();
            var subjects = await dashboard.GetAttentionQueueSubjectsAsync();

            Assert.Contains(subjects, t => t.Contains(overdueTitle, StringComparison.Ordinal));
            Assert.Contains(subjects, t => t.Contains(dueThisWeekTitle, StringComparison.Ordinal));

            // Overdue-first ordering: the overdue document's row must precede the due-this-week row.
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

    // ── Analytics charts (HeadcountByDepartmentChart / GenderSplitChart / EmploymentTypeSplitChart) ─
    // All three converted from Syncfusion donut charts to the shared HorizontalBarChart control
    // (or, for Headcount, a bespoke clickable "hbar-row--button" list) with plain-text,
    // non-truncated category labels — see src/HR.Web/Components/Controls/HorizontalBarChart.razor.
    // The seeded Acme company (Laura's company) has active employees with departments, genders
    // and employment types set, so these tests exercise the populated-chart path; there is no
    // seeded company with zero active employees available to this suite to exercise the
    // "No employee data available." empty state end-to-end without provisioning a brand-new
    // company, which is outside this suite's existing patterns.

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

        // At a desktop width, the grid should lay the three tiles out side by side (same row) —
        // assert they don't all stack vertically by checking their vertical positions roughly
        // coincide (within a small tolerance for border/padding rounding).
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

        // Accessible name must be the specific "View all employees" text, not a generic
        // "View all" — asserted via GetByRole with an exact name match, which throws if no
        // element in the widget has exactly that accessible name.
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

        // Acme has active employees with genders set, so this should render the chart, not the
        // empty state.
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

        // Acme has active employees with employment types set, so this should render the chart,
        // not the empty state.
        Assert.False(await dashboard.EmploymentTypeSplitChartIsEmptyAsync());

        var labels = await dashboard.GetEmploymentTypeSplitLabelsAsync();
        Assert.NotEmpty(labels);
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    // ── Accessibility: descriptive link/button names, keyboard focus ──────────

    [Fact]
    public async Task ViewAllLinks_HaveDescriptiveAccessibleNames_NotGenericViewAll()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHeadcountChartLoadedAsync();

        // "View all employees" — not a bare "View all".
        var headcountLink = _page.GetByRole(AriaRole.Link, new() { Name = "View all employees", Exact = true });
        await Assertions.Expect(headcountLink).ToBeVisibleAsync();

        // No element on the page should have exactly the generic accessible name "View all" —
        // every view-all-style link on this redesigned page should carry a more specific label
        // (e.g. "View all employees").
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

        var firstItem = _page.Locator(".attention-queue-item").First;
        if (!await firstItem.IsVisibleAsync())
        {
            // Empty queue for this run — nothing to tab to; covered by the "all clear" test.
            return;
        }

        // Queue rows are real <button> elements, so they're natively focusable and keyboard
        // activatable — .FocusAsync() plus asserting document.activeElement is simpler and more
        // reliable across browsers than simulating repeated Tab presses through the whole page.
        await firstItem.FocusAsync();

        var isFocused = await firstItem.EvaluateAsync<bool>("el => el === document.activeElement");
        Assert.True(isFocused, "Expected the first attention-queue row to be keyboard-focusable.");

        // :focus-visible outline rule in app.css applies to .task-widget-item — confirm the
        // computed outline is not "none" while focused.
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

    // ── Layout responsiveness ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1440, 900)]  // desktop
    [InlineData(834, 1112)]  // tablet
    [InlineData(390, 844)]   // mobile
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

    // ── Remaining sickness widgets ────────────────────────────────────────────
    // OverdueReturnToWorkReviewsWidget no longer renders standalone — its data now surfaces as
    // "Return-to-work review" category rows inside AttentionQueueWidget (see
    // AttentionQueueWidget.razor's `rtw` loop). CurrentSicknessAbsenceWidget and
    // MissingFitNotesWidget remain in the "More" section and still gate on
    // Session.CanManageEmployees (redundant with the route guard, but still asserted here for
    // completeness).

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

    // ── Favourite Reports Widget ──────────────────────────────────────────────
    // FavouriteReportsWidget.razor ("Favourite Reports") — lists whatever's been favourited from
    // the Reports catalog (ReportCatalogPage.razor's star toggle, see ReportCatalogTests.cs),
    // server-persisted via ReportingService's favourites endpoints. No favouriting UI of its own on
    // the dashboard, so these tests favourite via the catalog page first, same as
    // ReportCatalogTests.FavouriteToggle_PersistsAcrossReload_AndSortsFirstInCategory.

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
        // Self-heal rather than assert-and-fail: if an earlier run's assertion failure ever left
        // this favourited despite the try/finally below, asserting False unconditionally would
        // fail every subsequent run forever with no way to recover.
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
            // Leaves the persona's favourites clean for any other test relying on the seeded
            // dev database, mirroring the "no lingering test data" convention used elsewhere.
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
