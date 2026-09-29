using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CandidateListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates");
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 30_000 });
    }

    public async Task ClickNewCandidateAsync()
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (page.Url.Contains("/candidates/new"))
                return;

            try
            {
                await button.ClickAsync(new() { Timeout = attempt < maxAttempts ? 5_000 : 30_000 });
                await page.WaitForURLAsync("**/candidates/new**",
                    new()
                    {
                        Timeout = attempt < maxAttempts ? 5_000 : 30_000,
                        WaitUntil = WaitUntilState.Commit,
                    });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }

    public async Task<bool> HasCandidateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.HasGridCellOnAnyPageAsync(nameFragment);
    }

    public async Task ClickCandidateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        if (!await page.HasGridCellOnAnyPageAsync(nameFragment))
            throw new InvalidOperationException($"Candidate '{nameFragment}' was not found on any page of the list.");

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/candidates/**", new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
    }


    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ShowActiveOnlyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Active" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    private ILocator Row(string nameFragment) =>
        page.Locator(".e-row").Filter(new() { HasText = nameFragment }).First;

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await Row(nameFragment).Locator(".status-badge.status-badge--success").IsVisibleAsync();
    }
}
