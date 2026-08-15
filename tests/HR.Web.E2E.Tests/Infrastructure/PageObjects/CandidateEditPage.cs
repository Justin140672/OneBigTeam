using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the candidate create/edit/view page.
/// Routes: /companies/{id}/candidates/new, /candidates/{id}, /candidates/{id}/view
/// </summary>
public sealed class CandidateEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates/new");
        await page.WaitForSelectorAsync("input[placeholder='First name']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid candidateId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates/{candidateId}");
        await page.WaitForSelectorAsync("input[placeholder='First name']", new() { Timeout = 20_000 });
    }

    public async Task FillFirstNameAsync(string value)
    {
        await page.GetByPlaceholder("First name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillLastNameAsync(string value)
    {
        await page.GetByPlaceholder("Last name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEmailAsync(string value)
    {
        await page.GetByPlaceholder("candidate@example.com").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillPhoneAsync(string value)
    {
        await page.GetByPlaceholder("e.g. 07700 900000").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveNewCandidateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Returns true if the "hired and linked to an employee" banner is visible on the candidate detail page.</summary>
    public async Task<bool> HasHiredBannerAsync() =>
        await page.Locator(".alert-success:has-text('hired and linked')").IsVisibleAsync();

    public Task<string> GetFirstNameAsync() =>
        page.GetByPlaceholder("First name").InputValueAsync();

    // ── Close / unsaved-changes prompt (EditPageBase) ────────────────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
