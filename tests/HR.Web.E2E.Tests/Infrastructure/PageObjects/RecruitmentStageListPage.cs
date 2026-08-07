using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for RecruitmentStageList.razor (/companies/{companyId}/recruitment-stages,
/// ticket #100). Follows the same conventions as EmploymentTypeListPage: an HrGrid-based list with
/// a toolbar Add/Activate/Deactivate/Show Inactive set, plus per-row Move up/down reorder buttons
/// that don't exist on EmploymentType's list.
/// </summary>
public sealed class RecruitmentStageListPage(IPage page, string baseUrl)
{
    // Same rationale as EmploymentTypeListPage.RowsRenderedSelector: the EJ2 grid populates rows
    // asynchronously after the Blazor component mounts.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/recruitment-stages");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/recruitment-stages/new", new() { Timeout = 15_000 });
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
        // The Terminal Outcome column renders a StatusBadge in its own cell; grab the last badge-like
        // cell text in the row rather than the active-status badge (bg-success/bg-secondary) cell.
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
            // Name column is the row's link cell (second column, after Order).
            var link = rows.Nth(i).Locator("a").First;
            names.Add((await link.TextContentAsync())?.Trim() ?? "");
        }
        return names;
    }

    public async Task MoveUpAsync(string nameFragment)
    {
        await Row(nameFragment).Locator("button[title='Move up']").ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task MoveDownAsync(string nameFragment)
    {
        await Row(nameFragment).Locator("button[title='Move down']").ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task DeactivateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(nameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        // Opens a confirmation dialog (HrConfirmDialog) rather than deactivating immediately —
        // scoped to the dialog since its own confirm button shares the "Deactivate" label with
        // the toolbar button just clicked above.
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task ActivateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(nameFragment).ClickAsync();
        // Exact = true: "Activate" is a substring of "Deactivate", so without this the locator
        // resolves to both toolbar buttons (strict-mode violation) when a Deactivate button is
        // also present — same bug found in ExternalRecruiterListPage.cs.
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
