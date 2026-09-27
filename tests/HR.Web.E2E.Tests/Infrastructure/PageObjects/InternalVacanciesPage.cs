using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the self-service Internal Vacancies list
/// (src/HR.Web/Components/Pages/Recruitment/InternalVacancies.razor),
/// route /companies/{companyId}/internal-vacancies. Any authenticated employee of the company can
/// view it — no recruitment permission required. A vacancy appears here iff it belongs to the same
/// company AND Status == Open AND IsAdvertisedInternally == true.
/// </summary>
public sealed class InternalVacanciesPage(IPage page, string baseUrl)
{
    private const string CardSelector = "[data-testid='internal-vacancy-card']";
    private const string DetailSelector = "[data-testid='internal-vacancy-detail']";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/internal-vacancies");
        // With prerender disabled the page is blank until the interactive circuit connects — gate
        // on the authenticated shell first, then let WaitForInteractiveAsync settle the list.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits until the page's own heading has rendered and the list has settled onto either a
    /// populated card grid or the empty state (i.e. the "Loading vacancies…" indicator has gone).
    /// </summary>
    public async Task WaitForInteractiveAsync()
    {
        await page.GetByRole(AriaRole.Heading, new() { Name = "Internal Vacancies" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 30_000 });
    }

    /// <summary>Reads the page's main heading text.</summary>
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

    /// <summary>
    /// Types <paramref name="query"/> into the optional search box and waits for the list to
    /// re-render. The SfTextBox raises ValueChange on blur/change (not on the "input" event
    /// FillAsync dispatches), so an explicit blur is needed to actually trigger OnSearchChanged.
    /// </summary>
    public async Task SearchAsync(string query)
    {
        // SfTextBox splats data-testid onto its underlying <input> directly (no wrapper), so match
        // the input itself; keep the descendant form as a fallback.
        var search = page.Locator(
            "input[data-testid='internal-vacancy-search'], [data-testid='internal-vacancy-search'] input").First;
        await search.FillAsync(query);
        await search.PressAsync("Tab");
        // Give the server round-trip that reloads _items a moment, then wait for the list to
        // settle again on cards or the empty state.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync($"{CardSelector}, .vacancy-empty", new() { Timeout = 15_000 });
    }

    /// <summary>Clicks the vacancy card whose title matches <paramref name="title"/> and waits for the read-only detail dialog to open.</summary>
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

    /// <summary>
    /// Count of buttons in the detail dialog whose accessible name matches <paramref name="name"/>
    /// — used to assert the read-only dialog has no "Apply"/"Save" action.
    /// </summary>
    public Task<int> DetailButtonCountAsync(string name) =>
        DetailDialog.GetByRole(AriaRole.Button, new() { Name = name }).CountAsync();

    // ── Employee "Apply" flow ──────────────────────────────────────────────────────────────
    // Every locator below is a data-testid (unique on the page) — never a bare Syncfusion CSS
    // class, and never an [aria-xxx='true'] attribute match (Blazor bool-bound aria attributes are
    // unreliable to match on; use IsDisabledAsync / testids instead).

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

    /// <summary>
    /// Opens the vacancy card for <paramref name="title"/> via the keyboard (focus the
    /// role="button" card, press Enter) and waits for the detail dialog.
    /// </summary>
    public async Task OpenCardWithKeyboardAsync(string title)
    {
        var card = Card(title);
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.FocusAsync();
        await card.PressAsync("Enter");
        await Detail.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>Clicks the detail dialog's Apply button and waits for the apply form.</summary>
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

    /// <summary>Reads the read-only work email once the "Loading…" placeholder has been replaced.</summary>
    public async Task<string> GetApplicantEmailAsync()
    {
        await Assertions.Expect(ApplicantEmail).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(ApplicantEmail).Not.ToHaveTextAsync("Loading…", new() { Timeout = ServerRoundTripTimeoutMs });
        return ((await ApplicantEmail.TextContentAsync()) ?? "").Trim();
    }

    /// <summary>
    /// Number of editable form controls inside the apply form other than the CV file input — the
    /// applicant's identity is display-only, so this must be 0.
    /// </summary>
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

    /// <summary>
    /// Selects a CV the client should accept and waits until the server-side OnChange has recorded
    /// it (the "selected file" row shows the name) — so a following Submit can't race the upload
    /// selection and read "no file chosen".
    /// </summary>
    public async Task SelectValidCvAsync(string fileName, byte[] bytes, string mimeType = "application/pdf")
    {
        await SelectCvAsync(fileName, mimeType, bytes);
        await Assertions.Expect(CvSelected).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(CvFileName).ToHaveTextAsync(fileName, new() { Timeout = 15_000 });
    }

    public Task<bool> IsCvSelectedVisibleAsync() => CvSelected.IsVisibleAsync();

    /// <summary>Waits until the CV error region shows exactly <paramref name="expected"/>.</summary>
    public Task WaitForCvErrorAsync(string expected) =>
        Assertions.Expect(CvError).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    /// <summary>Polls until the CV error region is non-empty, then returns its text.</summary>
    public async Task<string> GetCvErrorAsync()
    {
        await Assertions.Expect(CvError).ToHaveTextAsync(NonWhitespace, new() { Timeout = 15_000 });
        return ((await CvError.TextContentAsync()) ?? "").Trim();
    }

    /// <summary>Clicks the apply form's Apply (submit) button. Callers wait for the specific outcome.</summary>
    public async Task SubmitApplicationAsync()
    {
        await SubmitButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await SubmitButton.ClickAsync();
    }

    /// <summary>Cancels the apply form and waits until the dialog is back on the vacancy details.</summary>
    public async Task CancelApplyAsync()
    {
        await CancelButton.ClickAsync();
        await Assertions.Expect(ApplyForm).ToBeHiddenAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(Detail).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>Polls until the server error region inside the apply form has text, then returns it.</summary>
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

    /// <summary>Instant (non-waiting) check — call only after the dialog has settled on a known state.</summary>
    public Task<int> AppliedStateCountAsync() => AppliedState.CountAsync();

    /// <summary>Waits for the disabled "Applied" footer button, then reports whether it is disabled.</summary>
    public async Task<bool> IsAppliedButtonDisabledAsync()
    {
        if (!await AppliedButton.WaitUntilVisibleAsync(ServerRoundTripTimeoutMs))
            return false;
        return await AppliedButton.IsDisabledAsync();
    }

    public Task<bool> IsApplyButtonVisibleAsync() => ApplyButton.WaitUntilVisibleAsync();

    /// <summary>Instant (non-waiting) count — call only after the dialog has settled on a known state.</summary>
    public Task<int> ApplyButtonCountAsync() => ApplyButton.CountAsync();

    public Task<bool> HasAppliedBadgeAsync(string title) =>
        Card(title).Locator("[data-testid='internal-vacancy-applied-badge']").WaitUntilVisibleAsync(ServerRoundTripTimeoutMs);

    /// <summary>Closes the detail dialog via its Close button and waits for it to go away.</summary>
    public async Task CloseDetailAsync()
    {
        await CloseButton.ClickAsync();
        await Assertions.Expect(Detail).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    /// <summary>The data-testid of the currently focused element (instant snapshot).</summary>
    public Task<string?> ActiveElementTestIdAsync() =>
        page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('data-testid') ?? null");

    /// <summary>
    /// Polls until the focused element carries <paramref name="testId"/> — focus is moved in the
    /// component's OnAfterRenderAsync, i.e. after the render patch lands, so an instant read right
    /// after the triggering action would race it. Returns false on timeout (caller asserts with
    /// <see cref="ActiveElementTestIdAsync"/> for a clear message).
    /// </summary>
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
