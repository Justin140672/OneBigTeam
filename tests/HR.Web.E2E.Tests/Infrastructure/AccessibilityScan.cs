using System.Linq;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// NFR-05 ("Add an automated accessibility quality gate"): the reusable axe-core assertion helper
/// used by every accessibility journey test in this suite. Generalises DSH-07's dashboard-only
/// inline axe block (see <c>AxeCoreDashboardScanTests</c>, now refactored to call this) into a
/// single entry point applied across authentication, self-service, administration, forms, dialogs,
/// data grids and reports.
///
/// Runs a WCAG 2.0 A / AA (<c>wcag2a</c> + <c>wcag2aa</c>) scan and treats any violation whose
/// impact is <c>serious</c> or <c>critical</c> as a build failure. Lower-impact (<c>minor</c> /
/// <c>moderate</c>) findings are reported by axe but do not fail the gate, matching DSH-07's
/// original threshold.
/// </summary>
public static class AccessibilityScan
{
    private static readonly string[] BlockingImpacts = ["serious", "critical"];

    /// <summary>
    /// Runs an axe-core <c>wcag2a</c>/<c>wcag2aa</c> scan of the current page state and asserts that
    /// it reports no <c>serious</c> or <c>critical</c> violations. On failure the assertion message
    /// names <paramref name="context"/> (the journey under test) and lists each blocking violation's
    /// id, impact, help text and failing node target(s)/HTML.
    /// </summary>
    public static async Task AssertNoSeriousViolationsAsync(IPage page, string context)
    {
        AxeResult results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa" },
            },
        });

        var blocking = SelectBlocking(
            results.Violations.Select(v => (v.Id, v.Impact ?? "", v.Help ?? "")));

        var detail = results.Violations
            .Where(v => BlockingImpacts.Contains(v.Impact ?? "", StringComparer.OrdinalIgnoreCase))
            .Select(v =>
            {
                var nodes = string.Join(
                    "\n      ",
                    (v.Nodes ?? Array.Empty<AxeResultNode>())
                        .Select(n => n.Target?.ToString() ?? n.Html ?? "(unknown node)"));
                return $"  - {v.Id} ({v.Impact}): {v.Help}\n      {nodes}";
            });

        Assert.True(blocking.Count == 0,
            $"axe-core reported {blocking.Count} serious/critical WCAG violation(s) during \"{context}\":\n" +
            string.Join("\n", detail));
    }

    /// <summary>
    /// Pure impact filter, extracted so the serious/critical selection logic is unit-testable in
    /// isolation from Playwright. Returns a formatted <c>"{id} ({impact}): {help}"</c> line for each
    /// violation whose impact is <c>serious</c> or <c>critical</c> (case-insensitive); everything
    /// else — including <c>minor</c>, <c>moderate</c>, empty and null impacts — is excluded.
    /// </summary>
    public static IReadOnlyList<string> SelectBlocking(IEnumerable<(string Id, string Impact, string Help)> violations) =>
        violations
            .Where(v => v.Impact is not null &&
                        BlockingImpacts.Contains(v.Impact, StringComparer.OrdinalIgnoreCase))
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help}")
            .ToList();
}
