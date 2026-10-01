using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Recruitment dashboard Insights tab: the Hiring Pipeline and New Hires charts' table
/// alternatives (ChartDataTableToggle), the pipeline legend (HiringPipelineChart.razor) and the
/// charts' accessible names / text summaries.
///
/// Acme's pipeline stages and counts are shared and mutated by other tests, so nothing here asserts
/// fixed stage names or counts: every expectation compares one rendering of the page against itself
/// (table vs legend vs the chart's aria-describedby summary). Each test seeds one application through
/// the API so the pipeline always has at least one non-zero stage.
/// </summary>
public sealed class RecruitmentInsightsChartTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string PipelineChartName = "Hiring pipeline: applications by stage";
    private const string NewHiresChartName = "New hires per month, last six months";

    private async Task SeedApplicationAsync()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);
    }

    private async Task OpenInsightsAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();
        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Insights);

        await Assertions.Expect(_page.GetByTestId("hiring-pipeline-table-toggle"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task<string> SummaryTextAsync(string chartTestId)
    {
        var describedBy = await _page.GetByTestId(chartTestId).GetAttributeAsync("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(describedBy), $"{chartTestId} has no aria-describedby.");
        var summary = _page.Locator($"#{describedBy}");
        await Assertions.Expect(summary).ToHaveCountAsync(1);
        return ((await summary.TextContentAsync()) ?? "").Trim();
    }

    private async Task<List<(string Label, string Value)>> TableRowsAsync(string tablePrefix)
    {
        var rows = _page.GetByTestId($"{tablePrefix}-table").Locator("tbody tr");
        var count = await rows.CountAsync();
        var result = new List<(string, string)>();
        for (var i = 0; i < count; i++)
        {
            var label = ((await rows.Nth(i).Locator("th").TextContentAsync()) ?? "").Trim();
            var value = ((await rows.Nth(i).Locator("td").TextContentAsync()) ?? "").Trim();
            result.Add((label, value));
        }

        return result;
    }

    private async Task ExpandAsync(string tablePrefix)
    {
        var toggle = _page.GetByTestId($"{tablePrefix}-table-toggle");
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(_page.GetByTestId($"{tablePrefix}-table")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("hiring-pipeline", "Stage", "Applications")]
    [InlineData("new-hires", "Month", "New hires")]
    public async Task TableToggle_IsCollapsedByDefault_ExpandsWithScopedHeaders_AndCollapsesAgain(
        string prefix, string categoryHeader, string valueHeader)
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();

        var toggle = _page.GetByTestId($"{prefix}-table-toggle");
        var table = _page.GetByTestId($"{prefix}-table");

        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-controls", new Regex(@"^chart-table-"));
        await Assertions.Expect(table).ToBeHiddenAsync();

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(table).ToBeVisibleAsync();

        var columnHeaders = table.Locator("thead th[scope='col']");
        await Assertions.Expect(columnHeaders).ToHaveTextAsync(new[] { categoryHeader, valueHeader });
        await Assertions.Expect(table.Locator("thead th")).ToHaveCountAsync(2);

        var bodyRows = table.Locator("tbody tr");
        var rowCount = await bodyRows.CountAsync();
        Assert.True(rowCount > 0, "Expected the table to contain at least one row.");
        await Assertions.Expect(table.Locator("tbody th[scope='row']")).ToHaveCountAsync(rowCount);
        await Assertions.Expect(table.Locator("tbody td")).ToHaveCountAsync(rowCount);

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(table).ToBeHiddenAsync();
    }

    [Fact]
    public async Task PipelineTable_RowsMatchLegendAndChartSummary_IncludingZeroCountStages()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();
        await ExpandAsync("hiring-pipeline");

        var rows = await TableRowsAsync("hiring-pipeline");
        var summary = await SummaryTextAsync("hiring-pipeline-chart");
        var legend = _page.GetByTestId("hiring-pipeline-legend-item");

        await Assertions.Expect(legend).ToHaveCountAsync(rows.Count);

        foreach (var (label, value) in rows)
        {
            Assert.Matches(@"^\d+$", value);
            Assert.Contains($"{label} {value}", summary);
            await Assertions.Expect(_page.Locator($"[data-testid='hiring-pipeline-legend-item'][data-stage='{label}']"))
                .ToHaveCountAsync(1);
        }

        Assert.Contains(rows, r => r.Value != "0");
        Assert.Contains($"Total {rows.Sum(r => int.Parse(r.Value))} applications.", summary);
    }

    [Fact]
    public async Task PipelineLegend_EveryItemHasDistinctShowStageAccessibleName()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();

        var legend = _page.GetByTestId("hiring-pipeline-legend");
        await Assertions.Expect(legend).ToHaveAttributeAsync("aria-label", "Pipeline stages shown in chart");

        var items = legend.GetByTestId("hiring-pipeline-legend-item");
        var count = await items.CountAsync();
        Assert.True(count > 0);

        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var stage = await items.Nth(i).GetAttributeAsync("data-stage");
            var name = await items.Nth(i).GetAttributeAsync("aria-label");
            Assert.Equal($"Show {stage}", name);
            names.Add(name!);

            await Assertions.Expect(legend.GetByRole(AriaRole.Button, new() { Name = $"Show {stage}", Exact = true }))
                .ToHaveCountAsync(1);
        }

        Assert.Equal(count, names.Distinct().Count());
    }

    [Fact]
    public async Task PipelineLegend_Toggle_FlipsPressedState_AndRemovesAndRestoresStageInChart()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();
        await ExpandAsync("hiring-pipeline");
        var populated = (await TableRowsAsync("hiring-pipeline")).First(r => r.Value != "0");
        var chart = _page.GetByTestId("hiring-pipeline-chart");
        var points = chart.Locator("path[id*='point' i]");
        var summaryBefore = await SummaryTextAsync("hiring-pipeline-chart");

        var item = _page.Locator($"[data-testid='hiring-pipeline-legend-item'][data-stage='{populated.Label}']");
        await Assertions.Expect(item).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(points.First).ToBeAttachedAsync(new() { Timeout = 15_000 });
        var pointCount = await points.CountAsync();

        await item.ClickAsync();
        await Assertions.Expect(item).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(points).ToHaveCountAsync(pointCount - 1, new() { Timeout = 15_000 });

        await item.ClickAsync();
        await Assertions.Expect(item).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(points).ToHaveCountAsync(pointCount, new() { Timeout = 15_000 });

        Assert.Equal(summaryBefore, await SummaryTextAsync("hiring-pipeline-chart"));
    }

    [Fact]
    public async Task PipelineLegend_HidingEveryStage_ShowsEmptyMessage_AndRestoringOneClearsIt()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();

        var items = _page.GetByTestId("hiring-pipeline-legend-item");
        var count = await items.CountAsync();
        var chart = _page.GetByTestId("hiring-pipeline-chart");
        var emptyMessage = chart.GetByText("All stages are hidden. Select a stage above to show it.");

        for (var i = 0; i < count; i++)
            await items.Nth(i).ClickAsync();

        await Assertions.Expect(_page.Locator("[data-testid='hiring-pipeline-legend-item'][aria-pressed='true']")).ToHaveCountAsync(0);
        await Assertions.Expect(emptyMessage).ToBeVisibleAsync();

        await items.First.ClickAsync();
        await Assertions.Expect(items.First).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(emptyMessage).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(1280, 900)]
    [InlineData(800, 900)]
    public async Task PipelineLegend_StageNamesAreNotTruncated(int width, int height)
    {
        await SeedApplicationAsync();
        await _page.SetViewportSizeAsync(width, height);
        await OpenInsightsAsync();

        var items = _page.GetByTestId("hiring-pipeline-legend-item");
        var count = await items.CountAsync();
        Assert.True(count > 0);

        for (var i = 0; i < count; i++)
        {
            var item = items.Nth(i);
            var stage = await item.GetAttributeAsync("data-stage");
            var text = item.Locator(".pipeline-legend-text");

            await Assertions.Expect(text).ToHaveTextAsync(stage!);

            var clipped = await text.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            var itemClipped = await item.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            Assert.False(clipped, $"Legend text for '{stage}' is clipped at {width}px.");
            Assert.False(itemClipped, $"Legend button for '{stage}' overflows at {width}px.");
        }
    }

    [Fact]
    public async Task Charts_HaveAccessibleNames_AndDescribedByTheirSummaryText()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();

        var panel = _page.Locator("#recruitment-tabpanel-insights");
        await Assertions.Expect(panel.GetByRole(AriaRole.Img, new() { Name = PipelineChartName, Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Img, new() { Name = NewHiresChartName, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var pipelineSummary = await SummaryTextAsync("hiring-pipeline-chart");
        Assert.Matches(@"Total \d+ applications\.$", pipelineSummary);

        var newHiresSummary = await SummaryTextAsync("new-hires-chart");
        Assert.StartsWith("New hires per month, last six months:", newHiresSummary);
        Assert.Matches(@"Total \d+ new hires\.$", newHiresSummary);
    }

    [Fact]
    public async Task NewHiresTable_RowsMatchChartSummary_IncludingZeroMonths()
    {
        await SeedApplicationAsync();
        await OpenInsightsAsync();

        await Assertions.Expect(_page.GetByTestId("new-hires-table-toggle"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ExpandAsync("new-hires");

        var rows = await TableRowsAsync("new-hires");
        var summary = await SummaryTextAsync("new-hires-chart");

        Assert.Equal(6, rows.Count);
        foreach (var (month, value) in rows)
        {
            Assert.Matches(@"^\d+$", value);
            Assert.Contains($"{month} {value}", summary);
        }

        Assert.Contains($"Total {rows.Sum(r => int.Parse(r.Value))} new hires.", summary);
    }
}
