using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class BulkInviteConfirmDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-invite-confirm-dialog");

    public Task WaitForVisibleAsync() =>
        Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

    public Task WaitForHiddenAsync() =>
        Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task<string> GetIntroTextAsync() =>
        (await Dialog.Locator("p.text-muted").First.InnerTextAsync()).Trim();

    private ILocator RecipientsTable => Dialog.Locator("table")
        .Filter(new() { HasNot = page.GetByRole(AriaRole.Columnheader, new() { Name = "Reason" }) });

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

    public Task<bool> HasExcludedSectionAsync() =>
        Dialog.GetByText("Excluded from this selection").IsVisibleAsync();

    private ILocator ExcludedTable => Dialog.Locator("table").Filter(new() { Has = page.GetByRole(AriaRole.Columnheader, new() { Name = "Reason" }) });

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

    /// <summary>
    /// The dialog's own error alert (BulkInviteConfirmDialog._error), rendered when queueing the
    /// batch fails — e.g. Ticket 9's work_email_required rejection when every selected employee
    /// has a public/personal work email. The dialog stays open in that case.
    /// </summary>
    public ILocator ErrorAlert => Dialog.Locator(".alert-danger");

    public Task ClickSendAsync() => SendButton.ClickAsync();

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
