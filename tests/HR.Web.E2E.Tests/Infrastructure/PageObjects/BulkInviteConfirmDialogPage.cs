using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for <c>BulkInviteConfirmDialog.razor</c> — the confirmation dialog opened by either
/// the invitation-mode candidate grid's or the normal employee grid's "Invite selected (N)"
/// action. Identified by its own CssClass, scoped to the role='dialog' element per this suite's
/// Playwright locator conventions (a bare ".bulk-invite-confirm-dialog" class selector risks
/// matching more than one Syncfusion-rendered node sharing the same CssClass).
/// </summary>
public sealed class BulkInviteConfirmDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-invite-confirm-dialog");

    public Task WaitForVisibleAsync() =>
        Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

    public Task WaitForHiddenAsync() =>
        Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>The intro paragraph, e.g. "2 employee(s) will be invited. Access granted is the standard Employee role."</summary>
    public async Task<string> GetIntroTextAsync() =>
        (await Dialog.Locator("p.text-muted").First.InnerTextAsync()).Trim();

    /// <summary>
    /// The recipients table, found by the ABSENCE of the excluded table's distinguishing "Reason"
    /// header column — NOT ".First": when every selected employee is excluded (Recipients.Count
    /// == 0), the recipients table doesn't render at all (see ExcludedTable's remarks), leaving
    /// only the excluded table on the page. ".First" would then silently match it instead —
    /// reading "excluded" rows back as if they were "recipients". This table only ever renders
    /// when Recipients.Count > 0, so no HasCountAsync == 0 case to handle beyond returning empty
    /// when it isn't present at all.
    /// </summary>
    private ILocator RecipientsTable => Dialog.Locator("table")
        .Filter(new() { HasNot = page.GetByRole(AriaRole.Columnheader, new() { Name = "Reason" }) });

    /// <summary>Names shown in the "recipients" table.</summary>
    public async Task<IReadOnlyList<string>> GetRecipientNamesAsync()
    {
        var cells = await RecipientsTable.Locator("tbody tr td:nth-child(1)").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<IReadOnlyList<string>> GetRecipientEmailsAsync()
    {
        var cells = await RecipientsTable.Locator("tbody tr td:nth-child(2)").AllAsync();
        var emails = new List<string>();
        foreach (var cell in cells)
            emails.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return emails;
    }

    /// <summary>True if the "Excluded from this selection" section is rendered.</summary>
    public Task<bool> HasExcludedSectionAsync() =>
        Dialog.GetByText("Excluded from this selection").IsVisibleAsync();

    /// <summary>
    /// The excluded-employees table, found by its distinguishing 3rd "Reason" header column —
    /// NOT by a fixed table index. BulkInviteConfirmDialog.razor's recipients table only renders
    /// at all when "@if (Recipients.Count > 0)" — a selection consisting entirely of excluded
    /// employees (e.g. a single already-has-account employee) renders ONLY this table, so an
    /// index-based ".Nth(1)" (assuming the recipients table always renders first) resolved to
    /// nothing and silently returned zero rows instead of failing loudly.
    /// </summary>
    private ILocator ExcludedTable => Dialog.Locator("table").Filter(new() { Has = page.GetByRole(AriaRole.Columnheader, new() { Name = "Reason" }) });

    /// <summary>Rows (Name, Email, Reason) of the excluded-employees table, when present.</summary>
    public async Task<IReadOnlyList<(string Name, string Email, string Reason)>> GetExcludedRowsAsync()
    {
        var rows = await ExcludedTable.Locator("tbody tr").AllAsync();
        var result = new List<(string, string, string)>();
        foreach (var row in rows)
        {
            var cells = await row.Locator("td").AllAsync();
            if (cells.Count < 3) continue;
            result.Add((
                (await cells[0].TextContentAsync())?.Trim() ?? "",
                (await cells[1].TextContentAsync())?.Trim() ?? "",
                (await cells[2].TextContentAsync())?.Trim() ?? ""));
        }
        return result;
    }

    private ILocator SendButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Send" });

    public Task<bool> IsSendButtonDisabledAsync() => SendButton.IsDisabledAsync();

    /// <summary>Clicks "Send N Invitation(s)" and waits for the dialog to close (batch queued).</summary>
    public async Task SendAsync()
    {
        await SendButton.ClickAsync();
        await WaitForHiddenAsync();
    }

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await WaitForHiddenAsync();
    }
}
