using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the anonymous Equality &amp; Diversity aggregate report
/// (/companies/{companyId}/reporting/equality-diversity — EqualityDiversityReportPage.razor).
///
/// This is the workforce-wide statistics report (HR Administrator, "reporting:view-equality"),
/// NOT the per-employee self-service tab (that is covered by EqualityDiversityTabTests). The page
/// is deliberately read-only: aggregated counts/percentages in summary cards plus one card per
/// monitoring dimension (each an SfChart + a small table). There is intentionally no drill-through
/// — no links out to individual employees — so that individual answers can never be reconstructed.
/// </summary>
public sealed class EqualityDiversityReportPage(IPage page, string baseUrl)
{
    public const string ContainerSelector = "[data-testid='equality-diversity-report']";

    private static readonly string[] DimensionKeys =
    [
        "gender", "age-band", "ethnicity", "disability",
        "sexual-orientation", "religion-or-belief", "caring-responsibilities",
    ];

    public string RouteFor(Guid companyId) =>
        $"{baseUrl}/companies/{companyId}/reporting/equality-diversity";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync(RouteFor(companyId));
        await page.WaitForSelectorAsync(
            $"{ContainerSelector} [data-testid='equality-total'], {ContainerSelector} .alert-danger",
            new() { Timeout = 20_000 });
    }

    private ILocator Container => page.Locator(ContainerSelector);

    public async Task<bool> IsContainerVisibleAsync() => await Container.IsVisibleAsync();

    public async Task<bool> HasLoadErrorAsync() =>
        await Container.Locator(".alert-danger").IsVisibleAsync();


    private ILocator Summary(string testId) => Container.Locator($"[data-testid='{testId}']");

    public Task<string> GetTotalTextAsync() => TrimmedTextAsync(Summary("equality-total"));
    public Task<string> GetRespondentsTextAsync() => TrimmedTextAsync(Summary("equality-respondents"));
    public Task<string> GetReportingDateTextAsync() => TrimmedTextAsync(Summary("equality-reporting-date"));

    public async Task<bool> AllSummaryCardsVisibleAsync() =>
        await Summary("equality-total").IsVisibleAsync()
        && await Summary("equality-respondents").IsVisibleAsync()
        && await Summary("equality-reporting-date").IsVisibleAsync();


    public ILocator DimensionCard(string key) =>
        Container.Locator($"[data-testid='equality-dimension-{key}']");

    public async Task<IReadOnlyList<string>> GetRenderedDimensionKeysAsync()
    {
        var present = new List<string>();
        foreach (var key in DimensionKeys)
        {
            if (await DimensionCard(key).CountAsync() > 0)
                present.Add(key);
        }
        return present;
    }

    public async Task<int> GetDimensionCardCountAsync() =>
        await Container.Locator("[data-testid^='equality-dimension-']").CountAsync();

    public async Task<bool> HasAnyChartRenderedAsync()
    {
        await page.WaitForSelectorAsync($"{ContainerSelector} .e-chart svg, {ContainerSelector} svg",
            new() { Timeout = 15_000 });
        return await Container.Locator(".e-chart svg, svg").CountAsync() > 0;
    }

    public async Task<int> GetDimensionTableRowCountAsync(string key) =>
        await DimensionCard(key).Locator("table tbody tr").CountAsync();


    public async Task<int> GetAnchorCountAsync() =>
        await Container.Locator("a").CountAsync();

    public async Task<int> GetDimensionAnchorCountAsync() =>
        await Container.Locator("[data-testid^='equality-dimension-'] a").CountAsync();

    public async Task<string> ClickFirstTableRowAndGetUrlAsync(string key)
    {
        var row = DimensionCard(key).Locator("table tbody tr").First;
        if (await row.CountAsync() > 0)
        {
            await row.ClickAsync();
            await page.WaitForTimeoutAsync(500);
        }
        return page.Url;
    }

    public async Task<string> ClickDimensionCardAndGetUrlAsync(string key)
    {
        await DimensionCard(key).ClickAsync();
        await page.WaitForTimeoutAsync(500);
        return page.Url;
    }

    private static async Task<string> TrimmedTextAsync(ILocator locator) =>
        (await locator.TextContentAsync())?.Trim() ?? "";
}
