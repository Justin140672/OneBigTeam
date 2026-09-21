using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for <c>InvitationBatchProgressPanel.razor</c> — the batch-progress panel rendered
/// on the Employee List page (both invite mode and the normal grid). Loads the latest batch for
/// the company on mount (GET .../invitation-batches/latest), and polls GET
/// .../invitation-batches/{batchId} every ~3s while the batch is Queued/Processing.
/// </summary>
public sealed class InvitationBatchProgressPanelPage(IPage page)
{
    private ILocator Root => page.Locator(".invitation-batch-progress-panel");

    public Task<bool> IsVisibleAsync() => Root.IsVisibleAsync();

    public Task WaitForVisibleAsync(int timeoutMs = 15_000) =>
        Root.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });

    /// <summary>The "processing continues even if you navigate away" hint, shown only while Queued/Processing.</summary>
    public Task<bool> HasProcessingContinuesMessageAsync() =>
        Root.GetByText("Processing continues even if you navigate away").IsVisibleAsync();

    public async Task<string> GetStatusLabelAsync() =>
        (await Root.Locator(".text-muted.ms-2").First.InnerTextAsync()).Trim();

    private async Task<int> ReadBadgeCountAsync(string label)
    {
        var badge = Root.Locator(".invitation-batch-counts .badge").Filter(new() { HasText = label }).First;
        var text = (await badge.InnerTextAsync()).Trim();
        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public Task<int> GetWaitingCountAsync() => ReadBadgeCountAsync("Waiting");
    public Task<int> GetProcessingCountAsync() => ReadBadgeCountAsync("Processing");
    public Task<int> GetSentCountAsync() => ReadBadgeCountAsync("Sent");
    public Task<int> GetSkippedCountAsync() => ReadBadgeCountAsync("Skipped");
    public Task<int> GetFailedCountAsync() => ReadBadgeCountAsync("Failed");

    public ILocator RetryFailedButton => Root.GetByRole(AriaRole.Button, new() { Name = "Retry failed invitations" });

    public Task<bool> HasRetryButtonAsync() => RetryFailedButton.IsVisibleAsync();

    public async Task ClickRetryFailedAsync()
    {
        await RetryFailedButton.ClickAsync();
        // ApplyUpdatedStatus's re-render is a Blazor Server round-trip; give it a moment before a
        // caller reads counts immediately after.
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Rows of the per-recipient table (Email, Status, Detail).</summary>
    public async Task<IReadOnlyList<(string Email, string Status, string Detail)>> GetRecipientRowsAsync()
    {
        var rows = await Root.Locator("table tbody tr").AllAsync();
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

    /// <summary>
    /// Polls the panel's own status label until it reports "Completed" (the batch's terminal
    /// state — see InvitationBatchProgressPanel.PollLoopAsync, which stops polling once the
    /// server reports Status == "Completed"), or the timeout elapses. CI-safe: polls the actual
    /// rendered outcome rather than sleeping a fixed duration, matching this suite's existing
    /// convention (e.g. EmployeeListPage.SearchAsync's settle-poll).
    /// </summary>
    public async Task WaitForCompletedAsync(int timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetStatusLabelAsync();
            if (status.Contains("Completed", StringComparison.OrdinalIgnoreCase))
                return;
            await page.WaitForTimeoutAsync(500);
        }
    }
}
