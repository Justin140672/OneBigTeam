using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SharedCompanyDocumentAcknowledgementPage(IPage page, string baseUrl)
{
    private ILocator AcknowledgedAlert => page.Locator(".alert-success").Filter(new() { HasText = "You acknowledged this document on" });

    public async Task GoToAsync(Guid companyId, Guid documentId, Guid? taskId = null)
    {
        var url = $"{baseUrl}/companies/{companyId}/shared-documents/published/{documentId}";
        if (taskId.HasValue)
            url += $"?taskId={taskId.Value}";

        await page.GotoAsync(url);
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync("h1, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").InnerTextAsync()).Trim();

    public Task<bool> IsAcknowledgedAsync() => AcknowledgedAlert.IsVisibleAsync();

    public async Task CheckConfirmationAsync()
    {
        var checkboxWrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "I confirm that I have read and understood this document." });
        await checkboxWrapper.Locator("label").ClickAsync();
    }

    public async Task ConfirmAcknowledgementAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Acknowledgement" }).ClickAsync();
        await AcknowledgedAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }
}
