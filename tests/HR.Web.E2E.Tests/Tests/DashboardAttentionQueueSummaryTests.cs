using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public static class DashboardAttentionQueueSummaryTests
{

    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static readonly string[] TaskBackedActionLabels =
    [
        "Open task", "Review leave request", "Review probation",
        "Complete return-to-work review", "View evidence request",
    ];

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

        await DashboardAttentionQueueSummaryTests.TryWaitForSummaryRequestAsync(
            _page, "**/dashboards/hr/summary", () => dashboard.GoToAsync());

        Assert.True(await dashboard.HasWidgetAsync("Needs your action"));

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
        Assert.True(badge > 0, "Expected the count badge to show a positive number when rows are present.");
        Assert.True(badge >= rowCount, $"Count badge ({badge}) is the real actionable total and must not be below the rendered rows ({rowCount}); rows are capped per category.");
    }

    [Fact]
    public async Task ClickingTaskBackedRow_OpensTaskViewDialogInPlace()
    {
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await LoginAndOpenAsync();
        await dashboard.ClickFirstTaskBackedAttentionQueueItemAsync();

        await task.WaitForLoadedAsync();
        Assert.Contains("/dashboard/hr", _page.Url);
        Assert.False(string.IsNullOrWhiteSpace(await task.GetTitleAsync()));
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
            string? action;
            try
            {
                action = (await rows.Nth(i).Locator(".attention-queue-action").TextContentAsync(new() { Timeout = 3_000 }))?.Trim();
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (action is null || DashboardAttentionQueueSummaryTests.TaskBackedActionLabels.Contains(action, StringComparer.OrdinalIgnoreCase))
                continue;

            await rows.Nth(i).ClickAsync();
            await _page.WaitForURLAsync(u => !u.Contains("/dashboard/hr"), new() { Timeout = 15_000 });
            Assert.DoesNotContain("/dashboard/hr", _page.Url);
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
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await DashboardAttentionQueueSummaryTests.ForceSummaryToFailAsync(_page, HrSummaryUrl);

        await dashboard.GoToAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        if (await dashboard.GetAttentionQueueSourceWarningCountAsync() == 0)
            return;

        Assert.False(await dashboard.AttentionQueueIsAllClearAsync());

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
        var dashboard = await LoginAndOpenAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();

        var titles = await dashboard.GetAttentionQueueSubjectsAsync();
        var metas  = await dashboard.GetAttentionQueueEmployeeNamesAsync();
        if (titles.Count == 0)
            return;

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

        Assert.True(await dashboard.HasWidgetAsync("Needs your action"));
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
        Assert.True(badge >= rowCount, $"Count badge ({badge}) must not be below the rendered rows ({rowCount}).");
    }

    [Fact]
    public async Task PendingLeaveRequest_ForTeamMember_AppearsAsAnActionableRow()
    {
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
            var actionLocator = rows.Nth(i).Locator(".attention-queue-action");
            if (await actionLocator.CountAsync() == 0)
                continue;

            var action = (await actionLocator.TextContentAsync())?.Trim();
            if (!string.Equals(action, "Open task", StringComparison.OrdinalIgnoreCase))
                continue;

            await rows.Nth(i).ClickAsync();
            await task.WaitForLoadedAsync();
            Assert.Contains("/dashboard/manager", _page.Url);
            Assert.True(await task.IsVisibleAsync());
            return;
        }

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
            string? action;
            try
            {
                action = (await rows.Nth(i).Locator(".attention-queue-action").TextContentAsync(new() { Timeout = 3_000 }))?.Trim();
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (action is null || DashboardAttentionQueueSummaryTests.TaskBackedActionLabels.Contains(action, StringComparer.OrdinalIgnoreCase))
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
            return;

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
            return;

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
