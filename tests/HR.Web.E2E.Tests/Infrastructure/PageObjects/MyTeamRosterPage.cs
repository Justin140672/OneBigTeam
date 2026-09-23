using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// "View all team" full roster page (src/HR.Web/Components/Pages/Employees/MyTeamRoster.razor).
/// Route: /companies/{CompanyId:guid}/my-team. Lists every direct/indirect report the manager
/// GetEmployeeTeamView endpoint still authorizes (Draft/Active/Suspended/Leaving —
/// FormerEmployee excluded), unlike the compact, Active-only, 8-card dashboard preview
/// (MyTeamWidget.razor).
/// </summary>
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
        // "td:first-child span" alone also matches ProfilePhotoAvatar's own initials span
        // (class="hr-profile-avatar hr-profile-avatar--initials", rendered before the name span
        // whenever the member has no photo) — excluding it, since otherwise each such row yields
        // two "names" (e.g. "ET" and the real full name) instead of one.
        var cells = await Rows.Locator("td:first-child span:not(.hr-profile-avatar--initials)").AllAsync();
        var names = new List<string>();
        foreach (var c in cells)
            names.Add((await c.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Switches between "Direct Reports" and "All Reports" scope, same control pattern as MyTeamWidget.</summary>
    public async Task SetScopeAsync(bool includeIndirect)
    {
        var label = includeIndirect ? "All Reports" : "Direct Reports";
        await page.GetByRole(AriaRole.Button, new() { Name = label }).ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await WaitForLoadedAsync();
    }

    /// <summary>Types into the search box (search-on-change, same SfTextBox wiring as EmployeeDirectory's).</summary>
    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        await SearchBox.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Clicks the "View profile" button on the row for <paramref name="employeeId"/>.</summary>
    public async Task ClickViewProfileAsync(Guid employeeId) =>
        await page.Locator($"[data-testid='roster-view-profile-{employeeId}']").ClickAsync();

    public async Task<bool> RowExistsAsync(Guid employeeId) =>
        await page.Locator($"[data-testid='roster-view-profile-{employeeId}']").IsVisibleAsync();
}
