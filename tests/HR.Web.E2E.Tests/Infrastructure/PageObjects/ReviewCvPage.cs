using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Review CV screen (ReviewCv.razor, ticket #1), route
/// /companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/review-cv. Guarded by
/// Session.CanManageRecruitment. Reached from the vacancy Kanban board candidate card menu
/// (VacancyKanbanBoardPage.ClickReviewCvFromCardMenuAsync) or the Applications tab row link
/// (VacancyDetailPage.ClickReviewCvForAsync), or navigated to directly via <see cref="GoToAsync"/>.
///
/// Stable hooks are the data-testid attributes added to ReviewCv.razor:
/// review-cv-page / review-cv-candidate-name / review-cv-position / review-cv-current-stage /
/// review-cv-notes / review-cv-save-notes / review-cv-move-forward / review-cv-reject /
/// review-cv-reject-reason / review-cv-close, plus the reject dialog located by its accessible name.
/// Internal recruitment Ticket 1 added review-cv-panel-title / review-cv-submitted-file-name /
/// review-cv-no-submitted-cv / review-cv-current-file-name for the submitted-vs-current CV panel.
/// Ticket 2 replaced the old candidate-only "Upload a CV" box with the application-scoped section
/// review-cv-application-actions (review-cv-replace-file-input / review-cv-replace-submit /
/// review-cv-use-current / review-cv-manage-candidate-cvs).
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

    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync("[data-testid='review-cv-page']", new() { Timeout = 30_000 });
        await NotesTextArea.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.Locator("[data-testid='review-cv-save-notes']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    public Guid GetApplicationIdFromUrl() => UrlIdParser.LastGuid(page.Url);


    public async Task<string?> GetCandidateNameAsync() =>
        (await Root.Locator("[data-testid='review-cv-candidate-name']").TextContentAsync())?.Trim();

    /// <summary>
    /// Internal recruitment Ticket 6: asserts whether the Internal badge sits beside the candidate
    /// name (inside dd.review-cv__candidate-name — the name span itself holds only the name). The name
    /// is awaited visible first so the "no badge" case is never satisfied by an unrendered summary.
    /// </summary>
    public async Task ExpectCandidateInternalBadgeAsync(bool isInternal)
    {
        var nameCell = Root.Locator("dd.review-cv__candidate-name");
        await Assertions.Expect(nameCell.Locator("[data-testid='review-cv-candidate-name']"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var badge = nameCell.Locator("[data-testid='internal-application-badge']");
        if (isInternal)
        {
            await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(badge).ToHaveTextAsync("Internal");
        }
        else
        {
            await Assertions.Expect(badge).ToHaveCountAsync(0);
        }
    }

    public async Task<string?> GetPositionAsync() =>
        (await Root.Locator("[data-testid='review-cv-position']").TextContentAsync())?.Trim();

    public async Task<string?> GetCurrentStageAsync() =>
        (await Root.Locator("[data-testid='review-cv-current-stage']").TextContentAsync())?.Trim();

    private ILocator NotesTextArea => page.Locator("[data-testid='review-cv-notes'] textarea");

    public Task<string> GetNotesAsync() => NotesTextArea.InputValueAsync();

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

    // ── CV panel (internal recruitment Ticket 1) ────────────────────────────────
    // The right-hand panel distinguishes the CV SUBMITTED with the application
    // (review-cv-submitted-file-name, title "Submitted CV") from the candidate's CURRENT CV shown as a
    // fallback (review-cv-no-submitted-cv warning banner + review-cv-current-file-name, title
    // "Candidate's current CV"), and from "no CV at all" (title "CV").
    // All of these render in the same pass as the notes textarea, so once WaitForLoadedAsync (or
    // ReplaceCvAsync / UseCurrentCvAsync, which wait for the post-action reload) has returned,
    // reading them is not racy.

    public const string SubmittedCvTitle = "Submitted CV";
    public const string CurrentCvTitle   = "Candidate's current CV";
    public const string NoCvTitle        = "CV";

    private ILocator CvPanelTitle        => Root.Locator("[data-testid='review-cv-panel-title']");
    private ILocator SubmittedFileName   => Root.Locator("[data-testid='review-cv-submitted-file-name']");
    private ILocator CurrentFileName     => Root.Locator("[data-testid='review-cv-current-file-name']");
    private ILocator NoSubmittedCvBanner => Root.Locator("[data-testid='review-cv-no-submitted-cv']");
    private ILocator NoCvMessage         => Root.GetByText("No CV has been uploaded for this candidate.");

    // Internal recruitment Ticket 2: application-scoped CV actions section (hidden when withdrawn).
    public const string ReplaceHeading = "Replace CV for this application";
    public const string UploadHeading  = "Upload CV for this application";

    public const string CvReplacedSuccess   = "CV replaced for this application.";
    public const string CvUploadedSuccess   = "CV uploaded for this application.";
    public const string UseCurrentCvSuccess = "The candidate's current CV is now recorded for this application.";

    private ILocator ApplicationActions     => Root.Locator("[data-testid='review-cv-application-actions']");
    private ILocator ActionsHeading         => ApplicationActions.Locator("h6");
    private ILocator ReplaceFileInput       => Root.Locator("input[type='file'][data-testid='review-cv-replace-file-input']");
    private ILocator ReplaceSubmitButton    => Root.Locator("[data-testid='review-cv-replace-submit']");
    private ILocator UseCurrentCvButton     => Root.Locator("[data-testid='review-cv-use-current']");
    private ILocator ManageCandidateCvsLink => Root.Locator("[data-testid='review-cv-manage-candidate-cvs']");
    private ILocator SuccessAlert           => Root.Locator(".alert-success");

    public async Task<string?> GetCvPanelTitleAsync() => (await CvPanelTitle.TextContentAsync())?.Trim();

    public Task ExpectCvPanelTitleAsync(string expected) =>
        Assertions.Expect(CvPanelTitle).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public Task<bool> IsSubmittedFileNameVisibleAsync() => SubmittedFileName.IsVisibleAsync();

    public async Task<string?> GetSubmittedFileNameAsync() => (await SubmittedFileName.TextContentAsync())?.Trim();

    public Task<bool> IsCurrentFileNameVisibleAsync() => CurrentFileName.IsVisibleAsync();

    public async Task<string?> GetCurrentFileNameAsync() => (await CurrentFileName.TextContentAsync())?.Trim();

    public Task<bool> IsNoSubmittedCvBannerVisibleAsync() => NoSubmittedCvBanner.IsVisibleAsync();

    public async Task<string?> GetNoSubmittedCvBannerTextAsync() => (await NoSubmittedCvBanner.TextContentAsync())?.Trim();

    public Task<bool> IsNoCvMessageVisibleAsync() => NoCvMessage.IsVisibleAsync();


    public Task<bool> IsApplicationCvActionsVisibleAsync() => ApplicationActions.IsVisibleAsync();

    public async Task<string?> GetReplaceHeadingAsync() => (await ActionsHeading.TextContentAsync())?.Trim();

    public Task ExpectReplaceHeadingAsync(string expected) =>
        Assertions.Expect(ActionsHeading).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public async Task<string?> GetReplaceSubmitTextAsync() => (await ReplaceSubmitButton.TextContentAsync())?.Trim();

    public Task<bool> IsUseCurrentCvVisibleAsync() => UseCurrentCvButton.IsVisibleAsync();

    public async Task ReplaceCvAsync(string fileName, byte[] content, string expectedSuccessText)
    {
        await ReplaceFileInput.SetInputFilesAsync(new FilePayload
        {
            Name     = fileName,
            MimeType = "application/pdf",
            Buffer   = content,
        });

        await Assertions.Expect(ReplaceSubmitButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await ReplaceSubmitButton.ClickAsync();

        await Assertions.Expect(SuccessAlert)
            .ToHaveTextAsync(expectedSuccessText, new() { Timeout = 30_000 });
    }

    public async Task UseCurrentCvAsync()
    {
        await Assertions.Expect(UseCurrentCvButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await UseCurrentCvButton.ClickAsync();

        await Assertions.Expect(SuccessAlert)
            .ToHaveTextAsync(UseCurrentCvSuccess, new() { Timeout = 30_000 });
    }

    public async Task ClickManageCandidateCvsAsync()
    {
        await ManageCandidateCvsLink.ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("/candidates/") && !u.Contains("/review-cv"), new() { Timeout = 30_000 });
    }


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
