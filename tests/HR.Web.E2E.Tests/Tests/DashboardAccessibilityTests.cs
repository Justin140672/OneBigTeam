using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// DSH-07 (dashboard accessibility + responsive). Covers the WAI-ARIA tabs keyboard pattern on the
/// Recruitment dashboard's Pipeline/Activity/Insights tablist, visible focus styling on interactive
/// dashboard controls, the visually-hidden polite aria-live status region rendered by
/// DashboardAnnouncer.razor on all three operational dashboards (/dashboard/hr, /dashboard/manager,
/// /dashboard/recruitment), a keyboard/screen-reader accessible table alternative for the Syncfusion
/// charts, and no horizontal overflow at narrow (375px) and tablet (768px) viewports.
///
/// A CrossUser-style class because it exercises three different role personas
/// (Laura Bennett = HR, James Okafor = Manager, Marcus Diallo = Recruiter) across its tests — none of
/// the four cached single-role fixtures covers all three dashboards.
/// </summary>
public sealed class DashboardAccessibilityTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    private const string HrEmail        = "laura.bennett@acme.example";
    private const string ManagerEmail   = "james.okafor@acme.example";
    private const string RecruiterEmail = "marcus.diallo@acme.example";

    private const double OverflowTolerancePx = 2;

    // ── Recruitment dashboard tablist: keyboard navigation ───────────────────

    [Fact]
    public async Task RecruitmentTabs_ArrowKeys_MoveSelectionAndDomFocus_WithWrapAndHomeEnd()
    {
        await LoginAsync(RecruiterEmail);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");
        await _page.WaitForSelectorAsync(".recruitment-dashboard-tabs", new() { Timeout = 20_000 });

        var tablist = _page.GetByRole(AriaRole.Tablist, new() { Name = "Recruitment dashboard sections" });
        await Assertions.Expect(tablist).ToBeVisibleAsync();

        var pipeline = _page.Locator("[data-testid='recruitment-tab-pipeline']");
        var activity = _page.Locator("[data-testid='recruitment-tab-activity']");
        var insights = _page.Locator("[data-testid='recruitment-tab-insights']");

        // Roving tabindex: only the active tab is in the tab order.
        await Assertions.Expect(pipeline).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(pipeline).ToHaveAttributeAsync("tabindex", "0");
        await Assertions.Expect(activity).ToHaveAttributeAsync("tabindex", "-1");

        await pipeline.FocusAsync();
        await _page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(activity).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(activity).ToBeFocusedAsync();
        await Assertions.Expect(pipeline).ToHaveAttributeAsync("aria-selected", "false");

        await _page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(insights).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(insights).ToBeFocusedAsync();

        // Wrap past the last tab back to the first.
        await _page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(pipeline).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(pipeline).ToBeFocusedAsync();

        // Home / End jump to the ends.
        await _page.Keyboard.PressAsync("End");
        await Assertions.Expect(insights).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(insights).ToBeFocusedAsync();

        await _page.Keyboard.PressAsync("Home");
        await Assertions.Expect(pipeline).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(pipeline).ToBeFocusedAsync();

        // ArrowLeft from the first tab wraps to the last.
        await _page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(insights).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(insights).ToBeFocusedAsync();
    }

    [Fact]
    public async Task RecruitmentTabs_HaveCorrectRolesAndAriaWiring()
    {
        await LoginAsync(RecruiterEmail);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");
        await _page.WaitForSelectorAsync(".recruitment-dashboard-tabs", new() { Timeout = 20_000 });

        foreach (var name in new[] { "pipeline", "activity", "insights" })
        {
            var tab = _page.Locator($"[data-testid='recruitment-tab-{name}']");
            await Assertions.Expect(tab).ToHaveAttributeAsync("role", "tab");
            await Assertions.Expect(tab).ToHaveAttributeAsync("id", $"recruitment-tab-{name}");
            await Assertions.Expect(tab).ToHaveAttributeAsync("aria-controls", $"recruitment-tabpanel-{name}");
        }

        // The visible panel is a tabpanel labelled by its owning tab.
        var pipelinePanel = _page.Locator("#recruitment-tabpanel-pipeline");
        await Assertions.Expect(pipelinePanel).ToHaveAttributeAsync("role", "tabpanel");
        await Assertions.Expect(pipelinePanel).ToHaveAttributeAsync("aria-labelledby", "recruitment-tab-pipeline");

        // Switching tab swaps which panel is rendered, still correctly wired.
        await _page.Locator("[data-testid='recruitment-tab-insights']").ClickAsync();
        var insightsPanel = _page.Locator("#recruitment-tabpanel-insights");
        await Assertions.Expect(insightsPanel).ToHaveAttributeAsync("role", "tabpanel");
        await Assertions.Expect(insightsPanel).ToHaveAttributeAsync("aria-labelledby", "recruitment-tab-insights");
    }

    // ── Visible focus indicator ─────────────────────────────────────────────

    [Fact]
    public async Task RecruitmentDashboard_KeyboardFocusedTab_HasVisibleFocusOutline()
    {
        await LoginAsync(RecruiterEmail);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");
        await _page.WaitForSelectorAsync(".recruitment-dashboard-tabs", new() { Timeout = 20_000 });

        var activity = _page.Locator("[data-testid='recruitment-tab-activity']");
        await _page.Locator("[data-testid='recruitment-tab-pipeline']").FocusAsync();
        await _page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(activity).ToBeFocusedAsync();

        var hasVisibleOutline = await activity.EvaluateAsync<bool>(
            @"el => {
                const s = getComputedStyle(el);
                const outlineVisible = s.outlineStyle !== 'none' &&
                    parseFloat(s.outlineWidth || '0') > 0;
                const boxShadowVisible = s.boxShadow && s.boxShadow !== 'none';
                return outlineVisible || boxShadowVisible;
            }");

        Assert.True(hasVisibleOutline,
            "Expected the keyboard-focused dashboard tab to show a visible focus indicator (outline or box-shadow)");
    }

    // ── Polite aria-live status region on all three operational dashboards ───

    [Theory]
    [InlineData(HrEmail, "/dashboard/hr")]
    [InlineData(ManagerEmail, "/dashboard/manager")]
    [InlineData(RecruiterEmail, "/dashboard/recruitment")]
    public async Task Dashboard_RendersPoliteAriaLiveRegion_WithNonEmptyTextAfterLoad(string email, string route)
    {
        await LoginAsync(email);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{route}");

        var liveRegion = _page.Locator("[aria-live='polite']").First;
        await liveRegion.WaitForAsync(new() { Timeout = 20_000 });

        // The dashboards populate the region once their data finishes loading — poll for non-empty text.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string text = "";
        while (DateTime.UtcNow < deadline)
        {
            text = (await liveRegion.InnerTextAsync())?.Trim() ?? "";
            if (text.Length > 0) break;
            await Task.Delay(250);
        }

        Assert.False(string.IsNullOrWhiteSpace(text),
            $"Expected the polite aria-live region on {route} to carry a status announcement after load");
        Assert.Contains("finished loading", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Chart table alternative ─────────────────────────────────────────────

    [Fact]
    public async Task RecruitmentInsightsCharts_ProvideAccessibleTableAlternative()
    {
        await LoginAsync(RecruiterEmail);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");
        await _page.WaitForSelectorAsync(".recruitment-dashboard-tabs", new() { Timeout = 20_000 });
        await _page.Locator("[data-testid='recruitment-tab-insights']").ClickAsync();

        var panel = _page.Locator("#recruitment-tabpanel-insights");
        var details = panel.Locator("details", new() { HasText = "as a table" }).First;
        await details.WaitForAsync(new() { Timeout = 20_000 });

        await Assertions.Expect(details.Locator("summary")).ToContainTextAsync(new Regex("View .* as a table"));
        await Assertions.Expect(details.Locator("table")).ToHaveCountAsync(1);
    }

    // NOTE: there is no "…ProvideAccessibleTableAlternative" test for the HR dashboard — its
    // insight tiles (Headcount by Department, Gender Split, Employment Type Split) render as
    // accessible HTML bar components (HorizontalBarChart / the custom headcount bars) with
    // aria-labels and visible values, not SVG/canvas Syncfusion charts, so there is no chart
    // needing a separate <details>/<table> alternative. The recruitment "Insights" tab still
    // uses a real SfChart — see RecruitmentInsightsCharts_ProvideAccessibleTableAlternative above.

    // ── Responsive: no horizontal overflow ─────────────────────────────────

    [Theory]
    [InlineData(HrEmail, "/dashboard/hr")]
    [InlineData(ManagerEmail, "/dashboard/manager")]
    [InlineData(RecruiterEmail, "/dashboard/recruitment")]
    public async Task Dashboard_NoHorizontalOverflow_AtMobileAndTabletViewports(string email, string route)
    {
        await LoginAsync(email);

        foreach (var (width, height) in new[] { (375, 800), (768, 1024) })
        {
            await _page.SetViewportSizeAsync(width, height);
            await _page.GotoAsync($"{_fixture.WebBaseUrl}{route}");
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var overflow = await _page.EvaluateAsync<double>(
                @"() => {
                    const el = document.scrollingElement || document.documentElement;
                    return el.scrollWidth - el.clientWidth;
                }");

            Assert.True(overflow <= OverflowTolerancePx,
                $"{route} at {width}x{height} overflows horizontally by {overflow}px (tolerance {OverflowTolerancePx}px)");
        }
    }

    [Fact]
    public async Task RecruitmentDashboardTabs_RemainVisible_AtMobileViewport()
    {
        await LoginAsync(RecruiterEmail);
        await _page.SetViewportSizeAsync(375, 800);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");

        await Assertions.Expect(_page.Locator(".recruitment-dashboard-tabs"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private async Task LoginAsync(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);
    }
}
