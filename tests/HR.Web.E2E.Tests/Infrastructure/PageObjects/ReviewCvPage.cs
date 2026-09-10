using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Review CV screen (ReviewCv.razor, ticket #1), route
/// /companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/review-cv. Guarded by
/// Session.CanManageRecruitment. Reached from the vacancy Kanban board applicant card menu
/// (VacancyKanbanBoardPage.ClickReviewCvFromCardMenuAsync) or the Applications tab row link
/// (VacancyDetailPage.ClickReviewCvForAsync), or navigated to directly via <see cref="GoToAsync"/>.
///
/// Stable hooks are the data-testid attributes added to ReviewCv.razor:
/// review-cv-page / review-cv-candidate-name / review-cv-position / review-cv-current-stage /
/// review-cv-notes / review-cv-save-notes / review-cv-move-forward / review-cv-reject /
/// review-cv-reject-reason / review-cv-close, plus the reject dialog located by its accessible name.
/// </summary>
public sealed class ReviewCvPage(IPage page, string baseUrl)
{
    private ILocator Root => page.Locator("[data-testid='review-cv-page']");

    public async Task GoToAsync(Guid companyId, Guid vacancyId, Guid applicationId, string? returnUrl = null)
    {
        var url = $"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/review-cv";
        if (returnUrl is not null)
            url += $"?returnUrl={Uri.EscapeDataString(returnUrl)}";
        await page.GotoAsync(url);
        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Waits until the page shell and its interactive controls have rendered — the container div,
    /// the notes textarea and the Save Notes button. With prerender disabled the page is blank
    /// until the interactive circuit connects, so gate on the shell first.
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync("[data-testid='review-cv-page']", new() { Timeout = 30_000 });
        await NotesTextArea.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.Locator("[data-testid='review-cv-save-notes']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    /// <summary>The application id parsed out of the current /review-cv URL (last GUID before the query string).</summary>
    public Guid GetApplicationIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    // ── Read-only candidate / vacancy / stage summary ───────────────────────────

    public async Task<string?> GetCandidateNameAsync() =>
        (await Root.Locator("[data-testid='review-cv-candidate-name']").TextContentAsync())?.Trim();

    public async Task<string?> GetPositionAsync() =>
        (await Root.Locator("[data-testid='review-cv-position']").TextContentAsync())?.Trim();

    public async Task<string?> GetCurrentStageAsync() =>
        (await Root.Locator("[data-testid='review-cv-current-stage']").TextContentAsync())?.Trim();

    // ── CV review notes ─────────────────────────────────────────────────────────
    // The notes field is an HrTextBox (SfTextBox Multiline) — a bare <textarea class="e-input">.
    private ILocator NotesTextArea => page.Locator("[data-testid='review-cv-notes'] textarea");

    public Task<string> GetNotesAsync() => NotesTextArea.InputValueAsync();

    /// <summary>
    /// Click-focus / select-all / delete / type-for-real / Tab-to-commit — the technique the rest
    /// of this suite uses for HrTextBox fields (see CandidateEditPage.SetPhoneAsync) so the typed
    /// value actually round-trips to the Blazor-bound model (SfTextBox commits on blur, not on the
    /// "input" event Playwright's FillAsync dispatches).
    /// </summary>
    public async Task SetNotesAsync(string value)
    {
        await NotesTextArea.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await NotesTextArea.PressSequentiallyAsync(value, new() { Delay = 15 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks Save Notes and waits for the "CV review notes saved." success alert. SaveNotesAsync
    /// in ReviewCv.razor sets _success and then re-fetches the application within the same handler,
    /// so the alert only appears once the save (and re-load) has landed.
    /// </summary>
    public async Task SaveNotesAsync()
    {
        await page.Locator("[data-testid='review-cv-save-notes']").ClickAsync();
        await Assertions.Expect(Root.Locator(".alert-success"))
            .ToHaveTextAsync("CV review notes saved.", new() { Timeout = 15_000 });
    }

    public Task<bool> HasSuccessAlertAsync() =>
        Root.Locator(".alert-success").IsVisibleAsync();

    public async Task<string?> GetGlobalErrorAsync()
    {
        var error = Root.Locator(".alert-danger");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Clicks Move Forward and waits for the client-side NavigateTo back to the origin (BackTarget)
    /// to leave the /review-cv route. On success ReviewCv.razor advances the application to the next
    /// active non-terminal stage and navigates away; on failure it shows a .alert-danger and stays.
    /// </summary>
    public async Task MoveForwardAsync()
    {
        await page.Locator("[data-testid='review-cv-move-forward']").ClickAsync();
        await WaitForNavigatedAwayAsync();
    }

    public async Task CloseAsync()
    {
        await page.Locator("[data-testid='review-cv-close']").ClickAsync();
        await WaitForNavigatedAwayAsync();
    }

    // ── Reject dialog ───────────────────────────────────────────────────────────
    private ILocator RejectDialog =>
        page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Reject Candidate" });

    /// <summary>
    /// Opens the Reject dialog, enters <paramref name="reason"/>, confirms, and waits for the
    /// client-side NavigateTo back to the origin. Uses the existing rejection workflow
    /// (ApplicationService.RejectCandidateAsync) server-side.
    /// </summary>
    public async Task RejectAsync(string reason)
    {
        await page.Locator("[data-testid='review-cv-reject']").ClickAsync();
        await RejectDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reasonBox = RejectDialog.Locator("[data-testid='review-cv-reject-reason'] textarea");
        await reasonBox.ClickAsync();
        await reasonBox.FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");

        await RejectDialog.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        await WaitForNavigatedAwayAsync();
    }

    private async Task WaitForNavigatedAwayAsync()
    {
        await page.WaitForURLAsync(u => !u.Contains("/review-cv"), new() { Timeout = 30_000 });
    }
}
