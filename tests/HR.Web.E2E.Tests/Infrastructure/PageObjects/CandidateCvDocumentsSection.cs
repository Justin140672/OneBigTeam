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
/// ("Submitted with N application(s)") / candidate-cv-empty /
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
    private ILocator FileInput    => Card.Locator("input[type='file'][data-testid='candidate-cv-file-input']");
    private ILocator UploadButton => Card.Locator("[data-testid='candidate-cv-upload']");
    private ILocator SuccessAlert => Card.Locator("[data-testid='candidate-cv-success']");
    private ILocator ErrorAlert   => Card.Locator("[data-testid='candidate-cv-error']");

    public Task WaitForLoadedAsync() =>
        Card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

    public Task<bool> IsEmptyStateVisibleAsync() => EmptyState.IsVisibleAsync();


    public Task ExpectRowCountAsync(int expected) =>
        Assertions.Expect(Rows).ToHaveCountAsync(expected, new() { Timeout = 15_000 });

    public Task<int> GetCurrentBadgeCountAsync() => CurrentBadges.CountAsync();

    public async Task<IReadOnlyList<string>> GetRowFileNamesAsync()
    {
        var names = await Rows.Locator("a").AllTextContentsAsync();
        return names.Select(n => n.Trim()).ToList();
    }

    private ILocator RowFor(string fileName) => Rows.Filter(new() { HasText = fileName });


    private ILocator RefreshButton => Card.Locator("[data-testid='candidate-documents-refresh']");

    public async Task<string?> GetRowScanStatusAsync(string fileName) =>
        await RowFor(fileName).Locator("[data-testid='candidate-document-scan-status']").GetAttributeAsync("data-scan-status");

    public Task<bool> IsRowDownloadLinkEnabledAsync(string fileName) =>
        RowFor(fileName).Locator("a[href]").IsVisibleAsync();

    public async Task<string?> GetRowDownloadHrefAsync(string fileName) =>
        await RowFor(fileName).Locator("a[href]").GetAttributeAsync("href");

    public Task<bool> IsRowDownloadDisabledAsync(string fileName) =>
        RowFor(fileName).Locator("[data-testid='candidate-document-download-disabled']").IsVisibleAsync();

    public Task ClickRefreshAsync() => RefreshButton.ClickAsync();

    public async Task WaitForRowScanStatusAsync(string fileName, string expectedStatus, int timeoutMs = 90_000)
    {
        var badge = RowFor(fileName).Locator(
            $"[data-testid='candidate-document-scan-status'][data-scan-status='{expectedStatus}']");
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            try
            {
                await badge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                return;
            }
            catch (System.TimeoutException) when (DateTime.UtcNow < deadline)
            {
                if (await RefreshButton.IsEnabledAsync())
                    await RefreshButton.ClickAsync();
            }
        }
    }

    public Task<bool> RowHasCurrentBadgeAsync(string fileName) =>
        RowFor(fileName).Locator("[data-testid='candidate-cv-current-badge']").IsVisibleAsync();

    public Task<bool> RowHasReferencedTextAsync(string fileName) =>
        RowFor(fileName).Locator("[data-testid='candidate-cv-referenced']").IsVisibleAsync();

    public Task ExpectRowReferencedTextAsync(string fileName, string expected) =>
        Assertions.Expect(RowFor(fileName).Locator("[data-testid='candidate-cv-referenced']"))
            .ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public async Task<string?> GetUploadButtonTextAsync() => (await UploadButton.TextContentAsync())?.Trim();

    public Task ExpectUploadButtonTextAsync(string expected) =>
        Assertions.Expect(UploadButton).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public async Task<string?> GetErrorAsync() =>
        await ErrorAlert.IsVisibleAsync() ? (await ErrorAlert.TextContentAsync())?.Trim() : null;

    public async Task UploadCvAsync(string fileName, byte[] content)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            await FileInput.SetInputFilesAsync(new FilePayload
            {
                Name     = fileName,
                MimeType = "application/pdf",
                Buffer   = content,
            });

            try
            {
                await Assertions.Expect(UploadButton).ToBeEnabledAsync(new() { Timeout = 5_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < maxAttempts)
            {
                await FileInput.SetInputFilesAsync(Array.Empty<FilePayload>());
            }
        }

        await UploadButton.ClickAsync();

        await Assertions.Expect(SuccessAlert)
            .ToHaveTextAsync(UploadSuccessText, new() { Timeout = 30_000 });
    }
}
