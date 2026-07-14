using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee-facing single-document acknowledgement page
/// (/companies/{companyId}/shared-documents/published/{documentId}),
/// SharedCompanyDocumentAcknowledgement.razor. This is the only place an employee actually
/// performs the acknowledgement action (checking the confirmation checkbox and clicking
/// "Confirm Acknowledgement") — the task panel (see TaskViewPage's "Shared document
/// acknowledgement panel" section) only links here rather than acknowledging inline.
/// </summary>
public sealed class SharedCompanyDocumentAcknowledgementPage(IPage page, string baseUrl)
{
    private ILocator AcknowledgedAlert => page.Locator(".alert-success").Filter(new() { HasText = "You acknowledged this document on" });

    /// <summary>
    /// Navigates directly to the acknowledgement page for the given document, optionally
    /// appending "?taskId=" (mirrors how SharedCompanyDocumentAcknowledgementTaskPanel's
    /// "View & Acknowledge Document" button links here) and waits for the loading state to
    /// resolve.
    /// </summary>
    public async Task GoToAsync(Guid companyId, Guid documentId, Guid? taskId = null)
    {
        var url = $"{baseUrl}/companies/{companyId}/shared-documents/published/{documentId}";
        if (taskId.HasValue)
            url += $"?taskId={taskId.Value}";

        await page.GotoAsync(url);
        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Waits for the page's loading state to resolve. GoToAsync already calls this; use it
    /// directly after arriving here via an in-app navigation (e.g. TaskViewPage's "View &amp;
    /// Acknowledge Document" button) rather than a fresh GotoAsync.
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync("h1, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>The document title shown in the page's &lt;h1&gt;.</summary>
    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").InnerTextAsync()).Trim();

    /// <summary>True once the "You acknowledged this document on …" success alert is showing.</summary>
    public Task<bool> IsAcknowledgedAsync() => AcknowledgedAlert.IsVisibleAsync();

    /// <summary>
    /// Checks the "I confirm that I have read and understood this document." SfCheckBox, using
    /// the same "click the wrapper's label" interaction this suite already uses for Syncfusion
    /// checkboxes (see SharedDocumentDetailPage.RequireAcknowledgementAsync).
    /// </summary>
    public async Task CheckConfirmationAsync()
    {
        var checkboxWrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "I confirm that I have read and understood this document." });
        await checkboxWrapper.Locator("label").ClickAsync();
    }

    /// <summary>
    /// Clicks "Confirm Acknowledgement" and waits for the page to reload its detail data and
    /// show the "You acknowledged this document on …" success state.
    /// </summary>
    public async Task ConfirmAcknowledgementAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Acknowledgement" }).ClickAsync();
        await AcknowledgedAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }
}
