using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LeaveTypeListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/leave-types");
        await page.EvaluateAsync(@"() => {
            window._listReady = false;
            let spinnerSeen = document.querySelector('.spinner-border') !== null;
            const obs = new MutationObserver(() => {
                if (!spinnerSeen && document.querySelector('.spinner-border')) {
                    spinnerSeen = true;
                }
                if (spinnerSeen && !document.querySelector('.spinner-border') &&
                    document.querySelector('.e-grid')) {
                    window._listReady = true;
                    obs.disconnect();
                }
            });
            obs.observe(document.body, { subtree: true, childList: true });
        }");
        await page.WaitForFunctionAsync(
            "window._listReady === true",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/leave-types/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasItemAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task DeactivateAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        await page.Locator(".e-toolbar-item[title='Deactivate']").ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        return await row.Locator(".badge.bg-success").IsVisibleAsync();
    }
}
