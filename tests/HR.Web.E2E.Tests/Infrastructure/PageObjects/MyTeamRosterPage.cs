using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class MyTeamRosterPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/my-team");
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync() =>
        await page.Locator("[data-testid='my-team-roster-table'], .widget-empty").First
            .WaitForAsync(new() { Timeout = 20_000 });

    private ILocator SearchBox => page.Locator(
        "input[data-testid='my-team-search'], [data-testid='my-team-search'] input").First;

    private ILocator Table => page.Locator("[data-testid='my-team-roster-table']");
    private ILocator Rows => Table.Locator("tbody tr");

    public async Task<int> RowCountAsync()
    {
        await WaitForLoadedAsync();
        return await Rows.CountAsync();
    }

    public async Task<IReadOnlyList<string>> GetRowNamesAsync()
    {
        await WaitForLoadedAsync();
        var cells = await Rows.Locator("td:first-child span:not(.hr-profile-avatar--initials)").AllAsync();
        var names = new List<string>();
        foreach (var c in cells)
            names.Add((await c.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task SetScopeAsync(bool includeIndirect)
    {
        var label = includeIndirect ? "All Reports" : "Direct Reports";
        await page.GetByRole(AriaRole.Button, new() { Name = label }).ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await WaitForLoadedAsync();
    }

    public Task ExpectRowCountAsync(int expected) =>
        Assertions.Expect(Rows).ToHaveCountAsync(expected, new() { Timeout = 15_000 });

    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        await SearchBox.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);
    }

    public async Task ClickViewProfileAsync(Guid employeeId) =>
        await page.Locator($"[data-testid='roster-view-profile-{employeeId}']").ClickAsync();

    public async Task<string> GetStatusBadgeTextAsync(Guid employeeId)
    {
        var row = Rows.Filter(new() { Has = page.Locator($"[data-testid='roster-view-profile-{employeeId}']") });
        return (await row.Locator("span.badge").First.TextContentAsync())?.Trim() ?? "";
    }

    public async Task<bool> RowExistsAsync(Guid employeeId) =>
        await page.Locator($"[data-testid='roster-view-profile-{employeeId}']").IsVisibleAsync();
}
