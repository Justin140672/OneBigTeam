using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class TaskViewPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid taskId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/tasks/{taskId}");
        await page.WaitForSelectorAsync("h1", new() { Timeout = 20_000 });
    }

    /// <summary>Returns the task title shown in the h1.</summary>
    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    /// <summary>Returns the task description paragraph text, or null if absent.</summary>
    public async Task<string?> GetDescriptionAsync()
    {
        var p = page.Locator(".card-body p.mb-3").First;
        return await p.IsVisibleAsync() ? (await p.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns the value of a detail row identified by its label text.</summary>
    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        if (!await dt.IsVisibleAsync()) return null;
        // The corresponding dd follows the dt in DOM order.
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }

    /// <summary>Returns the status badge text (e.g. "Not Started", "Completed").</summary>
    public async Task<string> GetStatusAsync() =>
        (await page.Locator(".task-status-badge").TextContentAsync())?.Trim() ?? "";

    /// <summary>Returns true if the "Task not found" error alert is displayed.</summary>
    public async Task<bool> IsNotFoundAsync() =>
        await page.Locator(".alert-danger").Filter(new() { HasText = "Task not found" }).IsVisibleAsync();

    // ── Leave review panel ───────────────────────────────────────────────────

    /// <summary>Returns true if the "Review Leave Request" card is present and active.</summary>
    public async Task<bool> HasLeaveReviewPanelAsync() =>
        await page.Locator(".card-header").Filter(new() { HasText = "Review Leave Request" }).IsVisibleAsync();

    public async Task EnterDecisionReasonAsync(string reason)
    {
        await page.GetByPlaceholder("Enter a reason for your decision…").FillAsync(reason);
    }

    public async Task ApproveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        // After approval the card is hidden and status badge changes.
        await page.WaitForSelectorAsync(".task-status-badge",
            new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "document.querySelector('.task-status-badge')?.textContent?.includes('Completed')",
            null, new() { Timeout = 15_000 });
    }

    public async Task RejectAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelector('.task-status-badge')?.textContent?.includes('Completed')",
            null, new() { Timeout = 15_000 });
    }

    /// <summary>Extracts the task GUID from the current page URL.</summary>
    public Guid GetTaskIdFromUrl()
    {
        var url   = page.Url;
        var parts = url.TrimEnd('/').Split('/');
        return Guid.Parse(parts[^1]);
    }

    // ── Probation review panel ───────────────────────────────────────────────

    /// <summary>Returns true if the "Complete Probation Review" card is present.</summary>
    public async Task<bool> HasProbationReviewPanelAsync() =>
        await page.Locator("[data-testid='probation-review-panel']").IsVisibleAsync();

    /// <summary>Returns the review type text shown in the probation review panel.</summary>
    public async Task<string?> GetProbationReviewTypeAsync()
    {
        var el = page.Locator("[data-testid='review-type']");
        return await el.IsVisibleAsync() ? (await el.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Fills the review notes textarea.</summary>
    public async Task EnterReviewNotesAsync(string notes) =>
        await page.GetByPlaceholder("Enter your review notes…").FillAsync(notes);

    /// <summary>Clicks "Complete Review" and waits for the status badge to show Completed.</summary>
    public async Task CompleteReviewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Complete Review" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelector('.task-status-badge')?.textContent?.includes('Completed')",
            null, new() { Timeout = 15_000 });
    }
}
