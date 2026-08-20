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
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
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
    // A bare IsVisibleAsync() snapshot right after navigating in can catch a transient
    // pre-load render pass — the "hired and linked" banner depends on the candidate detail
    // page's own async load of the just-created hire link, which can still be in flight the
    // instant after GoToAsync's wait condition (the First name input) is satisfied. Retry.
    public async Task<bool> HasHiredBannerAsync()
    {
        try
        {
            await Assertions.Expect(page.Locator(".alert-success:has-text('hired and linked')"))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

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
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    // ── Deactivate / Reactivate (CandidateDetail.razor) ──────────────────────────────

    /// <summary>Returns true if the "This candidate is inactive" alert banner is visible.</summary>
    public Task<bool> HasInactiveBannerAsync() =>
        page.Locator(".alert-secondary:has-text('inactive')").WaitUntilVisibleAsync();

    /// <summary>Text of the inactive banner, including the "Reason: ..." suffix when present.</summary>
    public Task<string?> GetInactiveBannerTextAsync() =>
        page.Locator(".alert-secondary:has-text('inactive')").TextContentAsync();

    /// <summary>Text of the action-error alert shown when a deactivate/reactivate call fails server-side.</summary>
    public async Task<string?> GetActionErrorAsync()
    {
        var locator = page.Locator(".alert-danger.alert-dismissible");
        if (!await locator.WaitUntilVisibleAsync())
            return null;
        return (await locator.TextContentAsync())?.Trim();
    }

    public Task ClickDeactivateAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true }).ClickAsync();

    public Task ClickReactivateAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Reactivate", Exact = true }).ClickAsync();

    // The deactivate dialog is a plain custom SfDialog (not HrConfirmDialog) with a header of
    // "Deactivate Candidate" and a <textarea class="form-control"> reason field — there is no
    // data-testid or role='dialog' name to anchor to besides the header text, so it's scoped via
    // :has-text on the dialog container the same way CandidateEditPage's UnsavedChangesDialog is.
    private ILocator DeactivateDialog => page.Locator("[role='dialog']:has-text('Deactivate Candidate')");

    public Task<bool> IsDeactivateDialogVisibleAsync() => DeactivateDialog.WaitUntilVisibleAsync();

    public Task FillDeactivateReasonAsync(string reason) =>
        DeactivateDialog.Locator("textarea.form-control").FillAsync(reason);

    /// <summary>
    /// Clicks the dialog's "Deactivate" confirm button without waiting for the dialog to close —
    /// for the client-side "reason is required" guard, which keeps the dialog open and shows an
    /// inline validation message instead of calling the API.
    /// </summary>
    public Task ClickConfirmDeactivateAsync() =>
        DeactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true }).ClickAsync();

    /// <summary>Confirms deactivation and waits for the dialog to close (successful deactivation).</summary>
    public async Task ConfirmDeactivateAndCloseAsync()
    {
        await ClickConfirmDeactivateAsync();
        await DeactivateDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task CancelDeactivateAsync() =>
        DeactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    /// <summary>
    /// True if the inline "A reason is required." validation message is visible inside the
    /// deactivate dialog (client-side guard in ConfirmDeactivateAsync).
    /// </summary>
    public Task<bool> HasDeactivateReasonErrorAsync() =>
        DeactivateDialog.Locator(".text-danger.small").WaitUntilVisibleAsync();

    // The reactivate dialog is an HrConfirmDialog with Title="Reactivate Candidate" and its own
    // confirm button labelled "Reactivate" (DangerConfirm="false", no "e-danger" styling) — scoped
    // the same way ExternalRecruiterListPage scopes its HrConfirmDialog confirm button, since the
    // toolbar/detail-page "Reactivate" button shares its accessible name with the dialog's own.
    private ILocator ReactivateDialog => page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Reactivate Candidate" });

    public Task<bool> IsReactivateDialogVisibleAsync() => ReactivateDialog.WaitUntilVisibleAsync();

    public async Task ConfirmReactivateAsync()
    {
        var confirmButton = ReactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Reactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await ReactivateDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task CancelReactivateAsync() =>
        ReactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
}
