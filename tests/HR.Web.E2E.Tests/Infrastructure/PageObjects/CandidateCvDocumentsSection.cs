using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "CV documents" card on the candidate details page (CandidateDetail.razor,
/// internal recruitment Ticket 2), route /companies/{companyId}/candidates/{id}. The card only
/// renders for an existing candidate, outside the candidate EditForm. Navigate with
/// <see cref="CandidateEditPage.GoToAsync"/> and then call <see cref="WaitForLoadedAsync"/>.
///
/// Stable hooks are the data-testid attributes on the card: candidate-cv-card / candidate-cv-row
/// (newest first) / candidate-cv-current-badge (newest row only) / candidate-cv-referenced
/// ("Submitted with N application(s)") / candidate-cv-empty / candidate-cv-legacy-link /
/// candidate-cv-file-input / candidate-cv-upload / candidate-cv-success / candidate-cv-error.
/// The "Current CV" badge also carries Bootstrap's bg-success class, so it is always located by its
/// data-testid, never by .badge.bg-success.
/// </summary>
public sealed class CandidateCvDocumentsSection(IPage page)
{
    public const string UploadSuccessText = "CV uploaded. It is now the candidate's current CV.";
    public const string UploadButtonText = "Upload CV";
    public const string UploadReplacementButtonText = "Upload replacement CV";

    private ILocator Card         => page.Locator("[data-testid='candidate-cv-card']");
    private ILocator Rows         => Card.Locator("[data-testid='candidate-cv-row']");
    private ILocator CurrentBadges => Card.Locator("[data-testid='candidate-cv-current-badge']");
    private ILocator EmptyState   => Card.Locator("[data-testid='candidate-cv-empty']");
    private ILocator LegacyLink   => Card.Locator("[data-testid='candidate-cv-legacy-link']");
    private ILocator FileInput    => Card.Locator("input[type='file'][data-testid='candidate-cv-file-input']");
    private ILocator UploadButton => Card.Locator("[data-testid='candidate-cv-upload']");
    private ILocator SuccessAlert => Card.Locator("[data-testid='candidate-cv-success']");
    private ILocator ErrorAlert   => Card.Locator("[data-testid='candidate-cv-error']");

    /// <summary>
    /// Waits for the card to render. The card is gated on the candidate having loaded
    /// (OnLoadedAsync also loads the CV list before the page leaves its loading state), so once
    /// the card is visible its rows / empty state are already populated.
    /// </summary>
    public Task WaitForLoadedAsync() =>
        Card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

    public Task<bool> IsEmptyStateVisibleAsync() => EmptyState.IsVisibleAsync();

    public Task<bool> IsLegacyLinkVisibleAsync() => LegacyLink.IsVisibleAsync();

    /// <summary>Auto-retrying assertion on the number of CV rows.</summary>
    public Task ExpectRowCountAsync(int expected) =>
        Assertions.Expect(Rows).ToHaveCountAsync(expected, new() { Timeout = 15_000 });

    public Task<int> GetCurrentBadgeCountAsync() => CurrentBadges.CountAsync();

    /// <summary>File names of the CV rows in display order (newest first).</summary>
    public async Task<IReadOnlyList<string>> GetRowFileNamesAsync()
    {
        var names = await Rows.Locator("a").AllTextContentsAsync();
        return names.Select(n => n.Trim()).ToList();
    }

    private ILocator RowFor(string fileName) => Rows.Filter(new() { HasText = fileName });

    public Task<bool> RowHasCurrentBadgeAsync(string fileName) =>
        RowFor(fileName).Locator("[data-testid='candidate-cv-current-badge']").IsVisibleAsync();

    public Task<bool> RowHasReferencedTextAsync(string fileName) =>
        RowFor(fileName).Locator("[data-testid='candidate-cv-referenced']").IsVisibleAsync();

    /// <summary>Auto-retrying (whitespace-normalised) assertion on a row's "Submitted with N application(s)" text.</summary>
    public Task ExpectRowReferencedTextAsync(string fileName, string expected) =>
        Assertions.Expect(RowFor(fileName).Locator("[data-testid='candidate-cv-referenced']"))
            .ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    /// <summary>"Upload CV" when there are no CVs yet, "Upload replacement CV" otherwise.</summary>
    public async Task<string?> GetUploadButtonTextAsync() => (await UploadButton.TextContentAsync())?.Trim();

    public Task ExpectUploadButtonTextAsync(string expected) =>
        Assertions.Expect(UploadButton).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public async Task<string?> GetErrorAsync() =>
        await ErrorAlert.IsVisibleAsync() ? (await ErrorAlert.TextContentAsync())?.Trim() : null;

    /// <summary>
    /// Selects an in-memory PDF named <paramref name="fileName"/> and clicks the upload button. The
    /// button is disabled until InputFile's OnChange has round-tripped to the circuit
    /// (_selectedCvFile set), so wait for it to become enabled rather than sleeping. UploadCvAsync in
    /// CandidateDetail.razor sets the success message and reloads the CV list inside one event
    /// handler, which only re-renders on completion — so the success alert appearing means the list
    /// already reflects the new upload.
    /// </summary>
    public async Task UploadCvAsync(string fileName, byte[] content)
    {
        await FileInput.SetInputFilesAsync(new FilePayload
        {
            Name     = fileName,
            MimeType = "application/pdf",
            Buffer   = content,
        });

        await Assertions.Expect(UploadButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await UploadButton.ClickAsync();

        await Assertions.Expect(SuccessAlert)
            .ToHaveTextAsync(UploadSuccessText, new() { Timeout = 30_000 });
    }
}
