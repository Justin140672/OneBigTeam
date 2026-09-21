using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for <c>InviteModeCandidateGrid.razor</c> — the candidate grid rendered on the
/// Employee List page when navigated in invitation mode (<c>?mode=invite</c>). Auto-selects every
/// candidate with a non-empty work email on load; candidates missing a work email are shown but
/// never auto-selected, with an inline explanation + edit link instead.
/// </summary>
public sealed class InviteModeCandidateGridPage(IPage page)
{
    private ILocator Root => page.Locator(".invite-mode-candidate-grid");

    private const string RowsRenderedSelector =
        ".invite-mode-grid .e-row, .invite-mode-grid .e-emptyrow";

    /// <summary>
    /// Waits for the candidate grid to finish loading (its own async fetch,
    /// UserAdministrationService.GetInvitableEmployeesAsync) AND for the component's
    /// auto-selection pass (OnAfterRenderAsync -> SelectEligibleIndexesAsync) to have settled —
    /// the "N selected" summary text is the only client-observable signal that pass has run at
    /// least once, since it starts at 0 and only becomes non-zero (when eligible candidates
    /// exist) once selection has actually committed.
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    private ILocator Row(string nameFragment) =>
        page.Locator(".invite-mode-grid .e-row").Filter(new() { HasText = nameFragment }).First;

    public async Task<bool> IsRowCheckedAsync(string nameFragment)
    {
        var input = Row(nameFragment).Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        return await input.IsCheckedAsync();
    }

    /// <summary>
    /// Toggles the checkbox for the row matching <paramref name="nameFragment"/> (checks it if
    /// unchecked, unchecks if checked — same click target either direction).
    /// </summary>
    public async Task ToggleRowAsync(string nameFragment)
    {
        var row = Row(nameFragment);
        var input = row.Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        var wasChecked = await input.IsCheckedAsync();

        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();

        // Mirrors EmployeeListPage.CheckEmployeeRowAsync's own reasoning: the click only updates
        // Syncfusion's client-side state immediately — RowSelected/RowDeselected (and the
        // downstream "N selected" recount) round-trip over Blazor Server.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await input.IsCheckedAsync() == wasChecked && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    /// <summary>
    /// True if the row matching <paramref name="nameFragment"/> shows the "No work email —
    /// correct this employee's record" inline explanation (InviteModeCandidateGrid's Work Email
    /// column template), rather than a plain email value.
    /// </summary>
    public async Task<bool> ShowsMissingEmailExplanationAsync(string nameFragment) =>
        await Row(nameFragment).GetByText("No work email", new() { Exact = false }).IsVisibleAsync();

    /// <summary>
    /// Href of the "correct this employee's record" edit link inside the missing-email
    /// explanation for the row matching <paramref name="nameFragment"/>.
    /// </summary>
    public async Task<string?> GetMissingEmailEditLinkHrefAsync(string nameFragment) =>
        await Row(nameFragment).GetByRole(AriaRole.Link).Last.GetAttributeAsync("href");

    /// <summary>Reads the "N selected" summary text next to the toolbar buttons.</summary>
    public async Task<int> GetSelectedCountAsync()
    {
        var text = (await Root.Locator("span.text-muted").First.InnerTextAsync()).Trim();
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public ILocator SelectAllEligibleButton => Root.GetByRole(AriaRole.Button, new() { Name = "Select all eligible" });
    public ILocator ClearSelectionButton => Root.GetByRole(AriaRole.Button, new() { Name = "Clear selection" });

    public async Task ClickSelectAllEligibleAsync()
    {
        await SelectAllEligibleButton.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    public async Task ClickClearSelectionAsync()
    {
        await ClearSelectionButton.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>The grid's own "Invite selected (N)" primary action button.</summary>
    public ILocator InviteSelectedButton =>
        Root.Locator("button").Filter(new() { HasTextRegex = new Regex(@"^Invite selected") }).First;

    public Task<bool> IsInviteSelectedButtonDisabledAsync() => InviteSelectedButton.IsDisabledAsync();

    public Task ClickInviteSelectedAsync() => InviteSelectedButton.ClickAsync();
}
