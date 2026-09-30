using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class InternalVacanciesPage(IPage page, string baseUrl)
{
    private const string CardSelector = "[data-testid='internal-vacancy-card']";
    private const string DetailSelector = "[data-testid='internal-vacancy-detail']";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/internal-vacancies");
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }

    public async Task WaitForInteractiveAsync()
    {
        await page.GetByRole(AriaRole.Heading, new() { Name = "Internal Vacancies" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 30_000 });
    }

    public async Task<string?> GetHeadingAsync()
    {
        var h1 = page.Locator("h1").First;
        await h1.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return (await h1.TextContentAsync())?.Trim();
    }

    public Task<int> CardCountAsync() => page.Locator(CardSelector).CountAsync();

    public Task<bool> HasCardAsync(string title) =>
        page.Locator(CardSelector).Filter(new() { HasText = title }).First.WaitUntilVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".vacancy-empty").WaitUntilVisibleAsync();

    public async Task SearchAsync(string query)
    {
        var search = page.Locator(
            "input[data-testid='internal-vacancy-search'], [data-testid='internal-vacancy-search'] input").First;
        await search.FillAsync(query);
        await search.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 15_000 });
    }

    public async Task OpenCardAsync(string title)
    {
        var card = page.Locator(CardSelector).Filter(new() { HasText = title }).First;
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.ClickAsync();
        await page.Locator(DetailSelector).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator DetailDialog =>
        page.GetByRole(AriaRole.Dialog).Filter(new() { Has = page.Locator(DetailSelector) });

    public Task<bool> IsDetailVisibleAsync() =>
        page.Locator(DetailSelector).WaitUntilVisibleAsync();

    public async Task<string?> GetDetailTitleAsync() =>
        (await page.Locator($"{DetailSelector} .vacancy-detail-title").TextContentAsync())?.Trim();

    public async Task<string?> GetDetailDescriptionAsync() =>
        (await page.Locator($"{DetailSelector} .vacancy-detail-description").TextContentAsync())?.Trim();

    public Task<int> DetailButtonCountAsync(string name) =>
        DetailDialog.GetByRole(AriaRole.Button, new() { Name = name }).CountAsync();


    private const int ServerRoundTripTimeoutMs = 20_000;
    private static readonly System.Text.RegularExpressions.Regex NonWhitespace = new(@"\S");

    private ILocator ByTestId(string testId) => page.Locator($"[data-testid='{testId}']");

    private ILocator Card(string title) => page.Locator(CardSelector).Filter(new() { HasText = title }).First;
    private ILocator Detail => page.Locator(DetailSelector);
    private ILocator ApplyForm => ByTestId("internal-apply-form");
    private ILocator ApplyButton => ByTestId("internal-vacancy-apply");
    private ILocator AppliedButton => ByTestId("internal-vacancy-applied-button");
    private ILocator AppliedState => ByTestId("internal-vacancy-applied-state");
    private ILocator CloseButton => ByTestId("internal-vacancy-close");
    private ILocator ApplicantName => ByTestId("apply-applicant-name");
    private ILocator ApplicantEmail => ByTestId("apply-applicant-email");
    private ILocator CvInput => ByTestId("internal-apply-cv-input");
    private ILocator CvSelected => ByTestId("internal-apply-cv-selected");
    private ILocator CvFileName => ByTestId("internal-apply-cv-file-name");
    private ILocator CvError => ByTestId("internal-apply-cv-error");
    private ILocator ServerError => ByTestId("internal-apply-error");
    private ILocator SubmitButton => ByTestId("internal-apply-submit");
    private ILocator CancelButton => ByTestId("internal-apply-cancel");
    private ILocator SuccessNotice => ByTestId("internal-apply-success");
    private ILocator AlreadyAppliedNotice => ByTestId("internal-apply-already-applied");

    public async Task OpenCardWithKeyboardAsync(string title)
    {
        var card = Card(title);
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.FocusAsync();
        await card.PressAsync("Enter");
        await Detail.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ClickApplyAsync()
    {
        await ApplyButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ApplyButton.ClickAsync();
        await ApplyForm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public Task<bool> IsApplyFormVisibleAsync() => ApplyForm.WaitUntilVisibleAsync();

    public async Task<string> GetApplicantNameAsync()
    {
        await Assertions.Expect(ApplicantName).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return ((await ApplicantName.TextContentAsync()) ?? "").Trim();
    }

    public async Task<string> GetApplicantEmailAsync()
    {
        await Assertions.Expect(ApplicantEmail).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(ApplicantEmail).Not.ToHaveTextAsync("Loading…", new() { Timeout = ServerRoundTripTimeoutMs });
        return ((await ApplicantEmail.TextContentAsync()) ?? "").Trim();
    }

    public async Task<int> CountEditableIdentityInputsAsync()
    {
        await ApplyForm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await ApplyForm
            .Locator("input:not([type='file']), textarea, select, [contenteditable='true']")
            .CountAsync();
    }

    /// <summary>
    /// Sets the CV file on the native &lt;input type=file&gt; rendered by Blazor's InputFile. Does
    /// not wait for any outcome — use <see cref="SelectValidCvAsync"/> for a file expected to be
    /// accepted, or <see cref="WaitForCvErrorAsync"/> for one expected to be rejected.
    /// </summary>
    public Task SelectCvAsync(string fileName, string mimeType, byte[] bytes) =>
        CvInput.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = mimeType, Buffer = bytes });

    public async Task SelectValidCvAsync(string fileName, byte[] bytes, string mimeType = "application/pdf")
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await SelectCvAsync(fileName, mimeType, bytes);
            try
            {
                await CvSelected.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                break;
            }
            catch (TimeoutException) when (DateTime.UtcNow < deadline)
            {
            }
        }

        await Assertions.Expect(CvSelected).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(CvFileName).ToHaveTextAsync(fileName, new() { Timeout = 15_000 });
    }

    public Task<bool> IsCvSelectedVisibleAsync() => CvSelected.IsVisibleAsync();

    public Task WaitForCvErrorAsync(string expected) =>
        Assertions.Expect(CvError).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public async Task<string> GetCvErrorAsync()
    {
        await Assertions.Expect(CvError).ToHaveTextAsync(NonWhitespace, new() { Timeout = 15_000 });
        return ((await CvError.TextContentAsync()) ?? "").Trim();
    }

    public async Task SubmitApplicationAsync()
    {
        await SubmitButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await SubmitButton.ClickAsync();
    }

    public async Task CancelApplyAsync()
    {
        await CancelButton.ClickAsync();
        await Assertions.Expect(ApplyForm).ToBeHiddenAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(Detail).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    public async Task<string> GetServerErrorAsync()
    {
        await Assertions.Expect(ServerError).ToHaveTextAsync(NonWhitespace, new() { Timeout = ServerRoundTripTimeoutMs });
        return ((await ServerError.TextContentAsync()) ?? "").Trim();
    }

    public Task<bool> IsSuccessVisibleAsync() => SuccessNotice.WaitUntilVisibleAsync(ServerRoundTripTimeoutMs);

    public async Task<string> GetSuccessTextAsync() =>
        ((await SuccessNotice.TextContentAsync()) ?? "").Trim();

    public Task<bool> IsAlreadyAppliedVisibleAsync() => AlreadyAppliedNotice.WaitUntilVisibleAsync(ServerRoundTripTimeoutMs);

    public async Task<string> GetAlreadyAppliedTextAsync() =>
        ((await AlreadyAppliedNotice.TextContentAsync()) ?? "").Trim();

    public Task<bool> IsAppliedStateVisibleAsync() => AppliedState.WaitUntilVisibleAsync(ServerRoundTripTimeoutMs);

    public Task<int> AppliedStateCountAsync() => AppliedState.CountAsync();

    public async Task<bool> IsAppliedButtonDisabledAsync()
    {
        if (!await AppliedButton.WaitUntilVisibleAsync(ServerRoundTripTimeoutMs))
            return false;
        return await AppliedButton.IsDisabledAsync();
    }

    public Task<bool> IsApplyButtonVisibleAsync() => ApplyButton.WaitUntilVisibleAsync();

    public Task<int> ApplyButtonCountAsync() => ApplyButton.CountAsync();

    public Task<bool> HasAppliedBadgeAsync(string title) =>
        Card(title).Locator("[data-testid='internal-vacancy-applied-badge']").WaitUntilVisibleAsync(ServerRoundTripTimeoutMs);

    public async Task CloseDetailAsync()
    {
        await CloseButton.ClickAsync();
        await Assertions.Expect(Detail).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    public Task<string?> ActiveElementTestIdAsync() =>
        page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('data-testid') ?? null");

    public async Task<bool> WaitForFocusAsync(string testId, int timeoutMs = 10_000)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "id => document.activeElement?.getAttribute('data-testid') === id",
                testId,
                new PageWaitForFunctionOptions { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
