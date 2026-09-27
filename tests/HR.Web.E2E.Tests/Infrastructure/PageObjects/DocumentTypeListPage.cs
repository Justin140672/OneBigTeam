using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the document type list page (/companies/{companyId}/document-types).
/// </summary>
public sealed class DocumentTypeListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/document-types");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForSpinnerToClearAsync();
        // ".e-grid" existing only means the component mounted — Syncfusion renders rows (and wires
        // the toolbar's click handling) on a later JS pass, so an "Add" click fired right after that
        // could be silently dropped and never navigate. Wait for the rendered grid body, as
        // PositionProfileListPage.GoToAsync already does.
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger",
            new() { Timeout = 30_000 });
    }

    public async Task ClickNewAsync()
    {
        // See LocatorExtensions.ClickGridAddAndWaitForCreateRouteAsync: the toolbar's click handling
        // is wired after the rows paint, so a first click can be dropped.
        await page.ClickGridAddAndWaitForCreateRouteAsync("**/document-types/new**");
    }

    /// <summary>The href of the grid row link whose text contains <paramref name="nameFragment"/> (e.g. "/companies/{id}/document-types/{id}").</summary>
    public async Task<string> GetRowHrefAsync(string nameFragment)
    {
        var href = await page.Locator(".e-rowcell a").Filter(new() { HasText = nameFragment }).First.GetAttributeAsync("href");
        return href ?? throw new InvalidOperationException($"No document-type row link found for '{nameFragment}'.");
    }

    public async Task<bool> HasItemAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();

    public async Task DeactivateAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        // Opens a confirmation dialog (HrConfirmDialog) rather than deactivating immediately —
        // scoped to the dialog since its own confirm button shares the "Deactivate" label with
        // the toolbar button just clicked above.
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        return await row.Locator(".status-badge.status-badge--success").IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger", new() { Timeout = 15_000 });
    }
}
