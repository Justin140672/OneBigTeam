using System.Linq;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class AccessibilityScan
{
    private static readonly string[] BlockingImpacts = ["serious", "critical"];

    public static async Task AssertNoSeriousViolationsAsync(IPage page, string context)
    {
        await WaitForGridsToSettleAsync(page);

        AxeResult results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa" },
            },
            Rules = new Dictionary<string, RuleOptions>
            {
                ["aria-required-children"] = new RuleOptions { Enabled = false },
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

    private static async Task WaitForGridsToSettleAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
        }

        var gridCount = await page.Locator(".e-grid").CountAsync();
        for (var i = 0; i < gridCount; i++)
        {
            var grid = page.Locator(".e-grid").Nth(i);
            try
            {
                await grid.Locator(".e-gridcontent .e-row, .e-gridcontent .e-emptyrow").First
                    .WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 10_000,
                    });
            }
            catch (TimeoutException)
            {
            }
        }

        if (gridCount > 0)
            await page.WaitForTimeoutAsync(500);
    }

    public static IReadOnlyList<string> SelectBlocking(IEnumerable<(string Id, string Impact, string Help)> violations) =>
        violations
            .Where(v => v.Impact is not null &&
                        BlockingImpacts.Contains(v.Impact, StringComparer.OrdinalIgnoreCase))
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help}")
            .ToList();
}
