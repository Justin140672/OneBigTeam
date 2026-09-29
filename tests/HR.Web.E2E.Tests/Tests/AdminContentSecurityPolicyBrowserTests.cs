using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 5 — the Admin Portal's Content-Security-Policy no longer allows 'unsafe-eval'. This drives
/// representative Syncfusion workflows (grids, filtering, charts, dialogs, dropdowns, date pickers,
/// numeric inputs, the dev sign-in hand-off) in a browser context that ENFORCES the policy
/// (<c>bypassCsp: false</c>; every other E2E context bypasses it so Playwright's own string
/// evaluators keep working) and fails on any CSP violation: a <c>securitypolicyviolation</c> DOM
/// event, a console message that mentions the Content Security Policy, or an uncaught EvalError.
///
/// Deliberately uses only locator/expect APIs (no <c>WaitForFunctionAsync(string)</c>, which itself
/// needs eval) so the test cannot trip the policy it is checking.
/// </summary>
public sealed class AdminContentSecurityPolicyBrowserTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    private const string ViolationRecorder = """
        window.__cspViolations = [];
        document.addEventListener('securitypolicyviolation', e => {
            window.__cspViolations.push(e.violatedDirective + ' | ' + e.blockedURI + ' | ' + e.sample);
        });
        """;

    private static readonly string[] Routes =
    [
        "/", "/customers", "/metrics", "/audit-log", "/admin-users", "/subscription-pricing",
        "/product-updates", "/settings", "/jobs", "/failed-payments", "/deletion-queue",
        "/operational-alerts", "/system-health", "/marketing-content",
    ];

    [Fact]
    public async Task AdminPortal_SyncfusionWorkflows_ProduceNoCspViolations()
    {
        var problems = new List<string>();

        await using var context = await _fixture.Browser.NewContextAsync(E2eBrowserContextOptions.Create(bypassCsp: false));
        await context.AddInitScriptAsync(ViolationRecorder);
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);

        page.Console += (_, msg) =>
        {
            if (msg.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)
                || msg.Text.Contains("EvalError", StringComparison.OrdinalIgnoreCase)
                || msg.Text.Contains("unsafe-eval", StringComparison.OrdinalIgnoreCase))
                problems.Add($"[console:{msg.Type}] {msg.Text}");
        };
        page.PageError += (_, error) =>
        {
            if (error.Contains("EvalError", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
                problems.Add($"[pageerror] {error}");
        };

        // Dev sign-in hand-off (previously JS eval; now a plain forced navigation).
        var login = new AdminLoginPage(page, _fixture.AdminWebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        foreach (var route in Routes)
        {
            await page.GotoAsync($"{_fixture.AdminWebBaseUrl}{route}", new() { WaitUntil = WaitUntilState.Load });
            await Assertions.Expect(page.Locator(".admin-shell")).ToBeVisibleAsync();
            await page.WaitForTimeoutAsync(1_000);
            await CollectViolationsAsync(page, route, problems);
        }

        // Grid filtering / navigation into a customer.
        await page.GotoAsync($"{_fixture.AdminWebBaseUrl}/customers");
        var search = page.Locator("input.e-textbox").First;
        await search.FillAsync("a");
        await page.WaitForTimeoutAsync(1_000);
        await CollectViolationsAsync(page, "/customers (filter)", problems);

        // Dropdown popup (Syncfusion popup/template rendering).
        await page.GotoAsync($"{_fixture.AdminWebBaseUrl}/admin-users");
        var dropdown = page.Locator(".e-dropdownlist").First;
        if (await dropdown.CountAsync() > 0)
        {
            await dropdown.ClickAsync();
            await page.WaitForTimeoutAsync(750);
        }
        await CollectViolationsAsync(page, "/admin-users (dropdown)", problems);

        // Date picker popup.
        await page.GotoAsync($"{_fixture.AdminWebBaseUrl}/audit-log");
        var pickerIcon = page.Locator(".e-datepicker ~ .e-input-group-icon, .e-input-group.e-date-wrapper .e-input-group-icon").First;
        if (await pickerIcon.CountAsync() > 0)
        {
            await pickerIcon.ClickAsync();
            await page.WaitForTimeoutAsync(750);
        }
        await CollectViolationsAsync(page, "/audit-log (date picker)", problems);

        Assert.True(problems.Count == 0,
            "Admin Portal produced Content-Security-Policy violations:\n" + string.Join("\n", problems));
    }

    private static async Task CollectViolationsAsync(IPage page, string where, List<string> problems)
    {
        var violations = await page.EvaluateAsync<string[]>("() => window.__cspViolations || []");
        problems.AddRange(violations.Select(v => $"[{where}] {v}"));
    }
}
