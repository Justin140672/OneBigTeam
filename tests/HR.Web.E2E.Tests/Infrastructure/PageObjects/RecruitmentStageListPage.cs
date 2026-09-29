using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class RecruitmentStageListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/recruitment-stages");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        // Retry the click rather than a single fire-and-wait: under real load (a full parallel
        // E2E run) the same navigation genuinely takes longer than a fixed single-shot timeout
        // often enough to fail — see EmployeeListPage.ClickNewEmployeeAsync, which already uses
        // this exact retry pattern for the same reason.
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // ClickAsync must be inside the try too — see EmployeeListPage.ClickNewEmployeeAsync's
                // remarks for why an unwrapped ClickAsync (default 30s actionability wait) can
                // escape the retry loop entirely and look like an unretried 30000ms timeout.
                await button.ClickAsync(new() { Timeout = attempt < maxAttempts ? 5_000 : 30_000 });
                await page.WaitForURLAsync("**/recruitment-stages/new**", new() { Timeout = attempt < maxAttempts ? 3_000 : 15_000 });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }

    public async Task<bool> HasItemAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    private ILocator Row(string nameFragment) =>
        page.Locator(".e-row").Filter(new() { HasText = nameFragment }).First;

    public Task ClickRowLinkAsync(string nameFragment) =>
        Row(nameFragment).Locator("a").ClickAsync();

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        // A terminal stage (e.g. "Hired") also has its own Terminal Outcome badge, which reuses
        // the same bg-success styling as the Active status badge when the outcome itself is
        // "positive" — scope to .First (the Active status badge, which always renders before the
        // Terminal Outcome cell) to avoid a strict-mode violation on rows with both.
        return await Row(nameFragment).Locator(".badge.bg-success").First.IsVisibleAsync();
    }

    public async Task<string?> GetTerminalOutcomeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var cells = Row(nameFragment).Locator(".e-rowcell");
        var count = await cells.CountAsync();
        for (var i = count - 1; i >= 0; i--)
        {
            var text = (await cells.Nth(i).TextContentAsync())?.Trim();
            if (text is "None" or "Hired" or "Rejected")
                return text;
        }
        return null;
    }

    public async Task<string?> GetPurposeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var cells = Row(nameFragment).Locator(".e-rowcell");
        var count = await cells.CountAsync();
        for (var i = count - 1; i >= 0; i--)
        {
            var text = (await cells.Nth(i).TextContentAsync())?.Trim();
            if (text is "New application" or "Interview" or "Offer")
                return text;
            if (text == "—")
                return "None";
        }
        return null;
    }

    public async Task<int?> GetDisplayOrderAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var firstCell = Row(nameFragment).Locator(".e-rowcell").First;
        var text = (await firstCell.TextContentAsync())?.Trim();
        return int.TryParse(text, out var value) ? value : null;
    }

    public async Task<IReadOnlyList<string>> GetNamesInOrderAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var rows = page.Locator(".e-grid .e-row");
        var count = await rows.CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var link = rows.Nth(i).Locator("a").First;
            names.Add((await link.TextContentAsync())?.Trim() ?? "");
        }
        return names;
    }

    public async Task MoveUpAsync(string nameFragment) => await MoveAsync(nameFragment, "Move up", delta: -1);

    public async Task MoveDownAsync(string nameFragment) => await MoveAsync(nameFragment, "Move down", delta: +1);

    private async Task MoveAsync(string nameFragment, string buttonTitle, int delta)
    {
        var namesBefore = await GetNamesInOrderAsync();
        var indexBefore = namesBefore.ToList().IndexOf(nameFragment);

        await Row(nameFragment).Locator($"button[title='{buttonTitle}']").ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var names = (await GetNamesInOrderAsync()).ToList();
            var index = names.IndexOf(nameFragment);
            if (index == indexBefore + delta) return;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Timed out waiting for '{nameFragment}' to move from index {indexBefore} to {indexBefore + delta} " +
                    $"after clicking '{buttonTitle}' — still at index {index}.");

            await page.WaitForTimeoutAsync(200);
        }
    }

    public async Task DeactivateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(nameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task ActivateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(nameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Activate", Exact = true });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public Task ShowInactiveAsync()
    {
        return ClickShowInactiveAsync();
    }

    private async Task ClickShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public Task<bool> HasActionErrorAsync() => page.Locator(".alert-danger").First.IsVisibleAsync();

    public async Task<string?> GetActionErrorTextAsync() =>
        (await page.Locator(".alert-danger").First.TextContentAsync())?.Trim();
}
