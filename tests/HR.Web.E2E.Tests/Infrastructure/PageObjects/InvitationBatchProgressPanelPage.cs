using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class InvitationBatchProgressPanelPage(IPage page)
{
    private ILocator Root => page.Locator(".invitation-batch-progress-panel");

    public Task<bool> IsVisibleAsync() => Root.IsVisibleAsync();

    public ILocator RecipientRows => Root.Locator("table tbody tr");

    public Task WaitForVisibleAsync(int timeoutMs = 15_000) =>
        Root.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });

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
        await page.WaitForTimeoutAsync(500);
    }

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
