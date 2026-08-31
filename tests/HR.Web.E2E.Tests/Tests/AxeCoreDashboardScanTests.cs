// DSH-07 adds this minimal axe-core scan for the three operational dashboards only. NFR-05 is the
// separate ticket that will generalise axe-core into a reusable quality gate across all pages — do
// not expand the scope here.

using System.Linq;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Runs an axe-core accessibility scan against /dashboard/hr, /dashboard/manager and
/// /dashboard/recruitment (the three operational dashboards touched by DSH-07) and fails on any
/// serious/critical WCAG violation. Compile-only in this repo's CI, like the rest of HR.Web.E2E.Tests.
/// </summary>
public sealed class AxeCoreDashboardScanTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    [Theory]
    [InlineData("laura.bennett@acme.example", "/dashboard/hr")]
    [InlineData("james.okafor@acme.example", "/dashboard/manager")]
    [InlineData("marcus.diallo@acme.example", "/dashboard/recruitment")]
    public async Task Dashboard_HasNoSeriousOrCriticalAxeViolations(string email, string route)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}{route}");
        await _page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        AxeResult results = await _page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa" },
            },
        });

        var blocking = results.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help}")
            .ToList();

        Assert.True(blocking.Count == 0,
            $"axe-core reported {blocking.Count} serious/critical violation(s) on {route}:\n" +
            string.Join("\n", blocking));
    }
}
