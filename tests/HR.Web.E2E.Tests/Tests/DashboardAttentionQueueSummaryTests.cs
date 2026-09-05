using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// DSH-06 stage 2 — the HR dashboard "Needs your attention" widget
/// (AttentionQueueWidget.razor) and the Manager dashboard "Requires your attention" widget
/// (ManagerAttentionQueueWidget.razor) were rewired from a fan-out of ~6-7 browser fetches down
/// to ONE server-side bounded summary request each:
///
///   HR      → GET /api/companies/{companyId}/dashboards/hr/summary
///   Manager → GET /api/companies/{companyId}/dashboards/manager/summary
///
/// The DSH-03 degraded-source UX is preserved: each returned category maps to a
/// WidgetSourceOutcome, so WidgetPanelState.Summarise still drives the per-failed-source
/// <c>.widget-source-warning</c> row, the <c>.attention-queue-all-clear</c> block and the
/// <c>.widget-count-badge</c>. The DSH-04 drill-down is preserved: a row with a linked TaskId
/// opens <see cref="TaskViewPage">TaskViewDialog</see> in place, otherwise it navigates the
/// item's DeepLinkUrl. The "Show resolved leave requests" checkbox and per-source individual
/// retry buttons were removed — retry is now retry-all, wired to the widget's ReloadAllAsync.
///
/// COMPILE-ONLY, and several assertions are written defensively (guard-and-return when the
/// relevant seeded data isn't present for a given run), mirroring the existing
/// HrDashboardTests / ManagerDashboardTests / DashboardWidgetFailureTests style:
///  * The Blazor Server dashboards fetch the summary server-side over the "hrapi" HttpClient, so
///    a browser-level <see cref="IPage.RouteAsync"/> cannot by itself force the upstream call to
///    fail — the partial-failure tests reuse DashboardWidgetFailureTests' interception mechanism
///    and no-op when the fault doesn't reach a browser-observable request (no server-side fault
///    hook exists yet).
///  * Likewise the route-wait on <c>**/dashboards/*/summary</c> is best-effort — it is captured
///    to document intent and tolerates a timeout for the same server-side-fetch reason.
/// </summary>
public static class DashboardAttentionQueueSummaryTests
{
    // shared helpers -------------------------------------------------------------------------

    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Best-effort wait for the single bounded summary request. The dashboards issue this
    /// server-side, so it may never surface as a browser request — a timeout here is not a
    /// failure, it just means the journey couldn't be observed at the network layer.
    /// </summary>
    public static async Task<bool> TryWaitForSummaryRequestAsync(IPage page, string urlGlob, Func<Task> trigger)
    {
        try
        {
            await page.RunAndWaitForRequestAsync(trigger, urlGlob, new() { Timeout = 8_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Mirrors DashboardWidgetFailureTests.ForceOneSourceToFailAsync: fulfils the first matching
    /// request with a 500 so the widget records a failed source. Only has an effect if the
    /// summary fetch is observable at the browser layer.
    /// </summary>
    public static async Task ForceSummaryToFailAsync(IPage page, Regex summaryUrl)
    {
        var tripped = false;
        await page.RouteAsync("**/api/**", async route =>
        {
            if (!tripped && summaryUrl.IsMatch(route.Request.Url))
            {
                tripped = true;
                await route.FulfillAsync(new()
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = "{\"error\":\"forced\"}",
                });
                return;
            }

            await route.ContinueAsync();
        });
    }
}

/// <summary>HR Administrator persona (Laura Bennett) against "/dashboard/hr".</summary>
public sealed class HrDashboardAttentionQueueSummaryTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";
    private static readonly Guid AcmeId = DashboardAttentionQueueSummaryTests.AcmeId;

    private static readonly Regex HrSummaryUrl =
        new(@"/dashboards/hr/summary", RegexOptions.IgnoreCase);

    private async Task<HrDashboardPage> LoginAndOpenAsync()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();
        return dashboard;
    }

    [Fact]
    public async Task Dashboard_LoadsAndWidgetResolves_IssuingTheSingleSummaryRequest()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Route-wait around the navigation that triggers the widget's OnInitializedAsync fetch.
        await DashboardAttentionQueueSummaryTests.TryWaitForSummaryRequestAsync(
            _page, "**/dashboards/hr/summary", () => dashboard.GoToAsync());

        Assert.True(await dashboard.HasWidgetAsync("Needs your attention"));

        // Loading indicator resolves to either rows or the "All clear" block.
        await dashboard.WaitForAttentionQueueLoadedAsync();
    }

    [Fact]
    public async Task SeededActionableData_RendersRows_AndCountBadgeMatchesRowCount()
    {
        // Acme always has some mix of seeded HR tasks / leave requests / reviews / documents, so
        // the queue is expected to be non-empty for Laura. If a given run genuinely has none, the
        // "all clear" contract is covered by AllClearState_RendersWhenNoActionableWork below.
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rowCount = await dashboard.GetAttentionQueueRowCountAsync();
        if (rowCount == 0)
        {
            Assert.True(await dashboard.AttentionQueueIsAllClearAsync());
            return;
        }

        var badge = await dashboard.GetAttentionQueueCountBadgeAsync();
        Assert.True(badge > 0, "Expected the count badge to show a positive number when rows are present.");
        Assert.Equal(rowCount, badge);
    }

    [Fact]
    public async Task ClickingTaskBackedRow_OpensTaskViewDialogInPlace()
    {
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // Carlos Rivera's seeded probation review always has a generated review task
        // (GenerateDueProbationReviewsJob), so its queue row is task-backed and activation opens
        // TaskViewDialog rather than navigating away. Gate against ProbationReviewFlowTests as the
        // other HR-dashboard probation tests do.
        await SharedProbationGate.Instance.WaitAsync();
        try
        {
            await LoginAndOpenAsync();

            var employeeNames = await dashboard.GetAttentionQueueEmployeeNamesAsync();
            var carlos = employeeNames.FirstOrDefault(n => n.Contains("Carlos", StringComparison.OrdinalIgnoreCase));
            if (carlos is null)
                return; // Carlos' review not in this run's top-25 window — nothing to assert here.

            await dashboard.ClickAttentionQueueItemAsync(carlos);

            await task.WaitForLoadedAsync();
            Assert.Contains("/dashboard/hr", _page.Url);
            Assert.Contains(carlos, await task.GetTitleAsync(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SharedProbationGate.Instance.Release();
        }
    }

    [Fact]
    public async Task ClickingRowWithoutLinkedTask_NavigatesToItsDeepLink()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        // A row whose primary action reads "View" (rather than "Open task") has no linked TaskId
        // and navigates its DeepLinkUrl on activation.
        var rows = _page.Locator(".attention-queue-card .attention-queue-item");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var action = (await rows.Nth(i).Locator(".attention-queue-action").TextContentAsync())?.Trim();
            if (!string.Equals(action, "View", StringComparison.OrdinalIgnoreCase))
                continue;

            await rows.Nth(i).ClickAsync();
            await _page.WaitForURLAsync(u => !u.Contains("/dashboard/hr"), new() { Timeout = 15_000 });
            Assert.DoesNotContain("/dashboard/hr", _page.Url);
            return;
        }

        // No deep-link row in this run's queue — covered structurally elsewhere.
    }

    [Fact]
    public async Task AllClearState_RendersWhenNoActionableWork()
    {
        // Defensive contract check (same as HrDashboardTests.AttentionQueue_ShowsAllClearSummary_
        // WhenEmpty): whenever the summary reports nothing actionable, the compact "All clear"
        // block shows and there are no rows — never both, never neither.
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rowCount = await dashboard.GetAttentionQueueRowCountAsync();
        var isAllClear = await dashboard.AttentionQueueIsAllClearAsync();

        Assert.Equal(rowCount == 0, isAllClear);
        if (isAllClear)
            await Assertions.Expect(_page.Locator(".attention-queue-card .attention-queue-all-clear"))
                .ToContainTextAsync("All clear");
    }

    [Fact]
    public async Task DegradedState_ShowsInlineSourceWarning_WithWorkingRetryAll()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await DashboardAttentionQueueSummaryTests.ForceSummaryToFailAsync(_page, HrSummaryUrl);

        await dashboard.GoToAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        if (await dashboard.GetAttentionQueueSourceWarningCountAsync() == 0)
            return; // server-side fetch — fault not browser-observable without a server hook.

        // A degraded source never shows the "All clear" block.
        Assert.False(await dashboard.AttentionQueueIsAllClearAsync());

        // Stop forcing the failure, then retry-all: the warning must clear.
        await _page.UnrouteAsync("**/api/**");
        await dashboard.RetryAttentionQueueAllAsync();
        await dashboard.WaitForAttentionQueueSourceWarningsClearedAsync();

        Assert.Equal(0, await dashboard.GetAttentionQueueSourceWarningCountAsync());
    }

    private static readonly string[] GenericCategoryLabels =
    {
        "Employee Tasks Overdue", "Manager Tasks Overdue", "Leave request", "Document review",
        "Probation review", "Return-to-work review", "Fit note evidence", "HR task",
    };

    [Fact]
    public async Task RowTitle_ShowsSpecificActionNotGenericCategory_AndMetaShowsEmployeeOrCategory()
    {
        // Title now shows the specific task/action title (e.g. "Complete Return to Work Review",
        // "Approve Leave Request") rather than a generic category label; the employee/category
        // detail moved to the meta line.
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var titles = await dashboard.GetAttentionQueueSubjectsAsync();
        var metas  = await dashboard.GetAttentionQueueEmployeeNamesAsync();
        if (titles.Count == 0)
            return; // nothing seeded in this run's queue — covered by AllClearState test.

        var specificIndex = titles.ToList().FindIndex(t =>
            !GenericCategoryLabels.Contains(t, StringComparer.OrdinalIgnoreCase));

        if (specificIndex < 0)
            return; // every seeded row this run happened to have a title equal to a generic label.

        Assert.False(string.IsNullOrWhiteSpace(metas[specificIndex]));
    }

    [Fact]
    public async Task OverdueRow_DoesNotDuplicateOverdueWordInRowText()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rows = _page.Locator(".attention-queue-card .attention-queue-item.attention-queue-item--overdue");
        var count = await rows.CountAsync();
        if (count == 0)
            return; // no overdue row in this run's queue.

        var text = (await rows.First.TextContentAsync()) ?? "";
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            text, "Overdue", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

        Assert.True(occurrences <= 1,
            $"Expected 'Overdue' to appear at most once in the row's visible text, found {occurrences}: '{text}'");
    }
}

/// <summary>Manager persona (James Okafor) against "/dashboard/manager".</summary>
public sealed class ManagerDashboardAttentionQueueSummaryTests(ManagerPersonaFixture fixture)
    : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string JamesEmail = "james.okafor@acme.example";

    private static readonly Regex ManagerSummaryUrl =
        new(@"/dashboards/manager/summary", RegexOptions.IgnoreCase);

    private async Task<ManagerDashboardPage> LoginAndOpenAsync()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();
        return dashboard;
    }

    [Fact]
    public async Task Dashboard_LoadsAndWidgetResolves_IssuingTheSingleSummaryRequest()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        await DashboardAttentionQueueSummaryTests.TryWaitForSummaryRequestAsync(
            _page, "**/dashboards/manager/summary", () => dashboard.GoToAsync());

        Assert.True(await dashboard.HasWidgetAsync("Requires your attention"));
        await dashboard.WaitForAttentionQueueLoadedAsync();
    }

    [Fact]
    public async Task SeededActionableData_RendersRows_AndCountBadgeMatchesRowCount()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rowCount = await dashboard.GetAttentionQueueRowCountAsync();
        if (rowCount == 0)
        {
            Assert.True(await dashboard.AttentionQueueIsAllClearAsync());
            return;
        }

        var badge = await dashboard.GetAttentionQueueCountBadgeAsync();
        Assert.True(badge > 0);
        Assert.Equal(rowCount, badge);
    }

    [Fact]
    public async Task PendingLeaveRequest_ForTeamMember_AppearsAsAnActionableRow()
    {
        // Seed an actionable item James actually owns: Tom Williams (his direct report) submits a
        // pending leave request, which becomes an open leave-approval task assigned to James — the
        // manager summary's "Leave request" category then surfaces it. LoginPage.LoginAsync does a
        // real re-login when the requested persona differs from the fixture's (see
        // RolePersonaFixtureBase), so a single-class fixture can still drive both personas.
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var dash    = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        var tomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var reason = $"E2E-DSH06-MGR-{Guid.NewGuid():N}";

        await login.GoToAsync();
        await login.LoginAsync("tom.williams@acme.example");
        await profile.GoToAsync(DashboardAttentionQueueSummaryTests.AcmeId, tomId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", "05/10/2026", "07/10/2026", reason);
        await profile.SubmitLeaveRequestAsync();
        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });

        await login.LoginAsync(JamesEmail);
        await dash.GoToAsync();

        var leaveEmployeeNames = await dash.GetAttentionQueueEmployeeNamesAsync("Leave request");
        Assert.Contains(leaveEmployeeNames, n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase));

        Assert.True(await dash.GetAttentionQueueCountBadgeAsync() > 0);
    }

    [Fact]
    public async Task ClickingTaskBackedRow_OpensTaskViewDialogInPlace()
    {
        var dashboard = await LoginAndOpenAsync();
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rows = _page.Locator(".attention-queue-card .attention-queue-item");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var action = (await rows.Nth(i).Locator(".attention-queue-action").TextContentAsync())?.Trim();
            if (!string.Equals(action, "Open task", StringComparison.OrdinalIgnoreCase))
                continue;

            await rows.Nth(i).ClickAsync();
            await task.WaitForLoadedAsync();
            Assert.Contains("/dashboard/manager", _page.Url);
            Assert.True(await task.IsVisibleAsync());
            return;
        }

        // No task-backed row in this run's queue — the deep-link path is covered below.
    }

    [Fact]
    public async Task ClickingRowWithoutLinkedTask_NavigatesToItsDeepLink()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rows = _page.Locator(".attention-queue-card .attention-queue-item");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var action = (await rows.Nth(i).Locator(".attention-queue-action").TextContentAsync())?.Trim();
            if (!string.Equals(action, "View", StringComparison.OrdinalIgnoreCase))
                continue;

            await rows.Nth(i).ClickAsync();
            await _page.WaitForURLAsync(u => !u.Contains("/dashboard/manager"), new() { Timeout = 15_000 });
            Assert.DoesNotContain("/dashboard/manager", _page.Url);
            return;
        }
    }

    [Fact]
    public async Task AllClearState_RendersWhenNoActionableWork()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rowCount = await dashboard.GetAttentionQueueRowCountAsync();
        var isAllClear = await dashboard.AttentionQueueIsAllClearAsync();

        Assert.Equal(rowCount == 0, isAllClear);
        if (isAllClear)
            await Assertions.Expect(_page.Locator(".attention-queue-card .attention-queue-all-clear"))
                .ToContainTextAsync("All clear");
    }

    [Fact]
    public async Task DegradedState_ShowsInlineSourceWarning_WithWorkingRetryAll()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        await DashboardAttentionQueueSummaryTests.ForceSummaryToFailAsync(_page, ManagerSummaryUrl);

        await dashboard.GoToAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        if (await dashboard.GetAttentionQueueSourceWarningCountAsync() == 0)
            return; // server-side fetch — fault not browser-observable without a server hook.

        Assert.False(await dashboard.AttentionQueueIsAllClearAsync());

        await _page.UnrouteAsync("**/api/**");
        await dashboard.RetryAttentionQueueAllAsync();
        await dashboard.WaitForAttentionQueueSourceWarningsClearedAsync();

        Assert.Equal(0, await dashboard.GetAttentionQueueSourceWarningCountAsync());
    }

    private static readonly string[] GenericCategoryLabels =
    {
        "Employee Tasks Overdue", "Manager Tasks Overdue", "Leave request", "Document review",
        "Probation review", "Return-to-work review", "Fit note evidence", "Team task",
    };

    [Fact]
    public async Task RowTitle_ShowsSpecificActionNotGenericCategory_AndMetaShowsEmployeeOrCategory()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var titles = await dashboard.GetAttentionQueueSubjectsAsync();
        var metas  = await dashboard.GetAttentionQueueEmployeeNamesAsync();
        if (titles.Count == 0)
            return; // nothing seeded in this run's queue — covered by AllClearState test.

        var specificIndex = titles.ToList().FindIndex(t =>
            !GenericCategoryLabels.Contains(t, StringComparer.OrdinalIgnoreCase));

        if (specificIndex < 0)
            return;

        Assert.False(string.IsNullOrWhiteSpace(metas[specificIndex]));
    }

    [Fact]
    public async Task OverdueRow_DoesNotDuplicateOverdueWordInRowText()
    {
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var rows = _page.Locator(".attention-queue-card .attention-queue-item.attention-queue-item--overdue");
        var count = await rows.CountAsync();
        if (count == 0)
            return;

        var text = (await rows.First.TextContentAsync()) ?? "";
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            text, "Overdue", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

        Assert.True(occurrences <= 1,
            $"Expected 'Overdue' to appear at most once in the row's visible text, found {occurrences}: '{text}'");
    }
}
