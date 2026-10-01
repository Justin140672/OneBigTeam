using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ResponsiveShellTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    public static TheoryData<int, string> WidthsAndExpectedModes => new()
    {
        { 1440, "wide" },
        { 1024, "compact" },
        { 850, "compact" },
        { 768, "compact" },
        { 390, "overlay" },
    };

    public static TheoryData<int, string> WidthsAndPages
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var width in new[] { 1440, 1024, 850, 768, 390 })
                foreach (var page in new[] { "dashboard", "employees", "profile", "user-admin", "report-catalogue", "headcount", "hr-settings" })
                    data.Add(width, page);
            return data;
        }
    }

    private async Task LoginAsync(int width)
    {
        await _page.SetViewportSizeAsync(width, 900);
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
    }

    private async Task OpenPageAsync(string page)
    {
        if (page == "profile")
        {
            await new EmployeeEditPage(_page, _fixture.WebBaseUrl)
                .GoToViewAsync(AcmeId, SeededE2eEmployees.ProfileViewEditMode.EmployeeId);
        }
        else
        {
            var path = page switch
            {
                "dashboard" => "/dashboard/hr",
                "employees" => $"/companies/{AcmeId}/employees",
                "user-admin" => $"/companies/{AcmeId}/user-administration",
                "report-catalogue" => $"/companies/{AcmeId}/reporting",
                "headcount" => $"/companies/{AcmeId}/reporting/hr-headcount-summary",
                "hr-settings" => $"/companies/{AcmeId}/hr-settings",
                _ => throw new ArgumentOutOfRangeException(nameof(page)),
            };
            await _page.GotoAsync($"{_fixture.WebBaseUrl}{path}");
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        await _page.Locator(".app-shell[data-nav-mode]:not([data-nav-mode='pending'])").WaitForAsync(new() { Timeout = 20_000 });
    }

    private Task<bool> HasDocumentLevelHorizontalOverflowAsync() => _page.EvaluateAsync<bool>(
        "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) > window.innerWidth + 1");

    [Theory]
    [MemberData(nameof(WidthsAndExpectedModes))]
    public async Task NavMode_FollowsViewportWidth(int width, string expectedMode)
    {
        await LoginAsync(width);
        await OpenPageAsync("employees");

        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-mode", expectedMode);
        await Assertions.Expect(_page.Locator(".app-shell"))
            .ToHaveAttributeAsync("data-nav-open", expectedMode == "wide" ? "true" : "false");
    }

    [Theory]
    [MemberData(nameof(WidthsAndPages))]
    public async Task Page_HasNoDocumentLevelHorizontalScroll_AndKeepsPrimaryControlsVisible(int width, string page)
    {
        await LoginAsync(width);
        await OpenPageAsync(page);

        Assert.False(await HasDocumentLevelHorizontalOverflowAsync(),
            $"{page} at {width}px must not scroll horizontally at document level");
        await Assertions.Expect(_page.Locator(".page-title")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Level = 1 }).First).ToBeVisibleAsync();

        var grid = _page.Locator(".e-grid").First;
        if (await grid.CountAsync() > 0 && await grid.IsVisibleAsync())
        {
            var box = await grid.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.X + box.Width <= width + 1, $"Grid on {page} at {width}px must stay inside the viewport and scroll internally");
        }
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(850)]
    [InlineData(768)]
    [InlineData(390)]
    public async Task ConstrainedViewport_NavIsCollapsed_OpensAsOverlay_AndEscapeRestoresFocus(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("employees");

        var sidebar = _page.Locator("#app-sidebar");
        var openButton = _page.Locator("#nav-open-button");
        var contentBefore = await _page.Locator(".main-content").BoundingBoxAsync();
        Assert.NotNull(contentBefore);
        Assert.True(contentBefore!.Width >= width - 1, "A closed drawer must not reserve sidebar width");

        await openButton.FocusAsync();
        await _page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(sidebar).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "true");
        var contentWhileOpen = await _page.Locator(".main-content").BoundingBoxAsync();
        Assert.Equal(contentBefore.Width, contentWhileOpen!.Width, 1);
        await Assertions.Expect(sidebar.Locator(":focus")).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        await _page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "false");
        await Assertions.Expect(openButton).ToBeFocusedAsync();
        Assert.False(await HasDocumentLevelHorizontalOverflowAsync());
    }

    [Fact]
    public async Task WideViewport_CollapseAndExpandNav_KeepsFocusAndContent()
    {
        await LoginAsync(1440);
        await OpenPageAsync("employees");

        var collapse = _page.GetByRole(AriaRole.Button, new() { Name = "Collapse navigation" });
        await collapse.FocusAsync();
        await _page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "false");
        await Assertions.Expect(_page.Locator("#nav-open-button")).ToBeFocusedAsync();

        await _page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "true");
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Collapse navigation" })).ToBeFocusedAsync();
        Assert.False(await HasDocumentLevelHorizontalOverflowAsync());
    }

    [Fact]
    public async Task ResizingFromWideToNarrow_CollapsesNav_AndRestoringWideRestoresExpandedPreference()
    {
        await LoginAsync(1440);
        await OpenPageAsync("employees");
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "true");

        await _page.SetViewportSizeAsync(850, 900);
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-mode", "compact");
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "false");

        await _page.SetViewportSizeAsync(1440, 900);
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-mode", "wide");
        await Assertions.Expect(_page.Locator(".app-shell")).ToHaveAttributeAsync("data-nav-open", "true");
    }

    [Theory]
    [InlineData(850)]
    [InlineData(390)]
    public async Task RouteChange_ResetsHorizontalScroll_AndKeepsLayoutStable(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("user-admin");
        await _page.EvaluateAsync("() => window.scrollTo(200, 0)");

        await OpenPageAsync("report-catalogue");

        Assert.Equal(0, await _page.EvaluateAsync<int>("() => Math.round(window.scrollX)"));
        Assert.False(await HasDocumentLevelHorizontalOverflowAsync());
    }

    [Theory]
    [InlineData(1440, 3)]
    [InlineData(850, 2)]
    [InlineData(390, 1)]
    public async Task ReportCatalogue_CardsReflowIntoColumns(int width, int maxColumns)
    {
        await LoginAsync(width);
        await OpenPageAsync("report-catalogue");
        await _page.WaitForSelectorAsync(".report-catalog-card", new() { Timeout = 20_000 });

        var distinctLefts = await _page.EvaluateAsync<int>(
            "() => new Set([...document.querySelectorAll('.report-card-grid')[0].children].map(c => Math.round(c.getBoundingClientRect().left))).size");

        Assert.InRange(distinctLefts, 1, maxColumns);
        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = "Search reports" })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(850)]
    [InlineData(390)]
    public async Task HeadcountSummary_KpiLabelsStayWhole_AndFiltersDoNotOverlap(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("headcount");
        await _page.WaitForSelectorAsync(".kpi-card", new() { Timeout = 20_000 });

        var clippedLabels = await _page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('.kpi-card .text-muted')].filter(e => e.scrollWidth > e.clientWidth + 1).length");
        Assert.Equal(0, clippedLabels);

        var overlappingFilters = await _page.EvaluateAsync<int>(@"() => {
            const rects = [...document.querySelectorAll('.report-filter-grid > div')].map(e => e.getBoundingClientRect());
            let overlaps = 0;
            for (let i = 0; i < rects.length; i++)
                for (let j = i + 1; j < rects.length; j++) {
                    const a = rects[i], b = rects[j];
                    if (a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1) overlaps++;
                }
            return overlaps;
        }");
        Assert.Equal(0, overlappingFilters);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(850)]
    [InlineData(390)]
    public async Task EmployeeProfile_IdentityAndActionsDoNotCompete_AndTabsAreReachable(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("profile");

        var overlap = await _page.EvaluateAsync<bool>(@"() => {
            const h = document.querySelector('.employee-profile-header h1')?.getBoundingClientRect();
            const a = document.querySelector('.employee-profile-actions')?.getBoundingClientRect();
            if (!h || !a) return false;
            return h.left < a.right - 1 && a.left < h.right - 1 && h.top < a.bottom - 1 && a.top < h.bottom - 1;
        }");
        Assert.False(overlap);

        var overviewTab = _page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }).First;
        await overviewTab.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(overviewTab).ToBeVisibleAsync();
        Assert.False(await HasDocumentLevelHorizontalOverflowAsync());
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(850)]
    [InlineData(390)]
    public async Task UserAdministration_SearchAndToolbarStayUsable(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("user-admin");

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = "Search users" });
        await Assertions.Expect(search).ToBeVisibleAsync();
        var box = await search.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.Width >= Math.Min(width - 48, 240), "The search box must be wide enough to show its placeholder");
        Assert.False(await HasDocumentLevelHorizontalOverflowAsync());
    }

    [Theory]
    [InlineData(850)]
    [InlineData(390)]
    public async Task Layout_SurvivesTheEquivalentOf200PercentZoom(int width)
    {
        await LoginAsync(width);
        await OpenPageAsync("employees");

        await _page.SetViewportSizeAsync(width / 2, 450);
        await _page.WaitForTimeoutAsync(300);

        Assert.False(
            await HasDocumentLevelHorizontalOverflowAsync(),
            "At 200% zoom (half the CSS pixels) there must be no document-level horizontal scroll");
    }
}
