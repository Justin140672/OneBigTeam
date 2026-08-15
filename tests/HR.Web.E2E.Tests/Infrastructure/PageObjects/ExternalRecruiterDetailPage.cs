using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the External Recruiter create/edit/view page
/// (/companies/{companyId}/external-recruiters/new|{id}|{id}/view, ExternalRecruiterDetail.razor),
/// including its soft duplicate-agency-name warning banner and read-only Activity Summary card.
/// </summary>
public sealed class ExternalRecruiterDetailPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/external-recruiters/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid recruiterId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/external-recruiters/{recruiterId}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task FillAgencyNameAsync(string value)
    {
        await page.GetByPlaceholder("e.g. Acme Recruiting").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillContactNameAsync(string value)
    {
        await page.GetByPlaceholder("Primary contact").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// True if the "Contact Name" field sits alone on its own full-width row (".col-12" — see
    /// ExternalRecruiterDetail.razor), rather than sharing a row with another field
    /// (e.g. previously paired 6-wide alongside another ".col-md-6" field).
    /// </summary>
    public async Task<bool> IsContactNameOnItsOwnRowAsync()
    {
        var field = page.GetByPlaceholder("Primary contact");
        var column = field.Locator("xpath=ancestor::div[contains(@class,'col-12')][1]");
        return await column.CountAsync() > 0;
    }

    public async Task FillContactEmailAsync(string value)
    {
        await page.GetByPlaceholder("contact@agency.com").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillContactTelephoneAsync(string value)
    {
        await page.GetByPlaceholder("e.g. 07700 900000").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetAgencyNameAsync() =>
        page.GetByPlaceholder("e.g. Acme Recruiting").InputValueAsync();

    /// <summary>
    /// Blurs the Agency Name field (tabbing to the next field) — required to trigger the
    /// non-blocking duplicate-agency-name check (ExternalRecruiterDetail.razor's OnAgencyNameBlurAsync).
    /// </summary>
    public async Task BlurAgencyNameAsync()
    {
        await page.GetByPlaceholder("e.g. Acme Recruiting").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
    }

    private ILocator DuplicateWarning => page.Locator("[data-testid='agency-name-duplicate-warning']");

    public Task<bool> IsDuplicateWarningVisibleAsync() => DuplicateWarning.IsVisibleAsync();

    public async Task<string?> GetDuplicateWarningTextAsync() =>
        await DuplicateWarning.IsVisibleAsync() ? (await DuplicateWarning.TextContentAsync())?.Trim() : null;

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/external-recruiters", new() { Timeout = 15_000 });
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

    // ── Activity Summary (existing recruiter only) ───────────────────────────────

    private ILocator ActivitySummaryCard => page.Locator("[data-testid='recruiter-activity-summary']");

    public Task<bool> IsActivitySummaryVisibleAsync() => ActivitySummaryCard.IsVisibleAsync();

    public async Task<string?> GetCandidatesIntroducedCountAsync()
    {
        var value = ActivitySummaryCard.Locator(".text-muted.small", new() { HasText = "Candidates Introduced" })
            .Locator("xpath=following-sibling::div[1]");
        return await value.IsVisibleAsync() ? (await value.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetCandidatesHiredCountAsync()
    {
        var value = ActivitySummaryCard.Locator(".text-muted.small", new() { HasText = "Candidates Hired" })
            .Locator("xpath=following-sibling::div[1]");
        return await value.IsVisibleAsync() ? (await value.TextContentAsync())?.Trim() : null;
    }

    public Task<bool> HasCurrentVacancyAsync(string vacancyTitleFragment) =>
        ActivitySummaryCard.GetByText("Current Vacancies")
            .Locator("xpath=following-sibling::ul[1]")
            .GetByText(vacancyTitleFragment, new() { Exact = false })
            .IsVisibleAsync();

    public Task<bool> HasPreviousVacancyAsync(string vacancyTitleFragment) =>
        ActivitySummaryCard.GetByText("Previous Vacancies")
            .Locator("xpath=following-sibling::ul[1]")
            .GetByText(vacancyTitleFragment, new() { Exact = false })
            .IsVisibleAsync();

    // ── Unsaved changes dialog (EditPageBase, shared convention) ─────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() => UnsavedChangesDialog.WaitUntilVisibleAsync();
}
