using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the External Recruiter admin list page
/// (/companies/{companyId}/external-recruiters, ExternalRecruiterList.razor). Follows the same
/// SearchPageBase-driven grid/toolbar conventions as EmploymentTypeListPage — a row must be
/// selected before the "Activate"/"Deactivate" toolbar buttons act on it.
/// </summary>
public sealed class ExternalRecruiterListPage(IPage page, string baseUrl)
{
    // ".e-grid" alone doesn't prove rows are queryable — see EmploymentTypeListPage's identical
    // reasoning; Syncfusion's EJ2 grid populates rows on its own JS tick after Blazor mounts.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/external-recruiters");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/external-recruiters/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasItemAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = agencyNameFragment })
            .First
            .IsVisibleAsync();
    }

    private ILocator Row(string agencyNameFragment) =>
        page.Locator(".e-row").Filter(new() { HasText = agencyNameFragment }).First;

    public async Task<bool> IsActiveAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await Row(agencyNameFragment).Locator(".badge.bg-success").IsVisibleAsync();
    }

    public async Task DeactivateAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(agencyNameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ActivateAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await Row(agencyNameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Activate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Clicks the given recruiter's agency-name link, navigating to its view/edit page.</summary>
    public async Task ClickRecruiterAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a").Filter(new() { HasText = agencyNameFragment }).First;
        await link.ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Save'), button:has-text('Close')", new() { Timeout = 20_000 });
    }
}
