using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for RecruitmentStageEdit.razor (/companies/{companyId}/recruitment-stages/new and
/// /{id}, ticket #100). Mirrors EmploymentTypeEditPage's pattern.
/// </summary>
public sealed class RecruitmentStageEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/recruitment-stages/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid stageId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/recruitment-stages/{stageId}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Screening, Interview, Offered").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetNameAsync() =>
        page.GetByPlaceholder("e.g. Screening, Interview, Offered").InputValueAsync();

    /// <summary>Selects the Terminal Outcome value ("None", "Hired" or "Rejected") via the shared DropDownSelector.</summary>
    public Task SelectTerminalOutcomeAsync(string outcome)
    {
        var scope = page.Locator(".card-body");
        return DropDownSelector.SelectAsync(page, scope, outcome);
    }

    /// <summary>
    /// Selects the Purpose value ("None", "New application", "Interview" or "Offer") via the shared
    /// DropDownSelector. The Purpose field only renders for a non-terminal stage
    /// (RecruitmentStageEdit.razor's <c>@if (!Model.IsTerminal)</c>), so set Terminal Outcome to
    /// "None" first if needed.
    /// </summary>
    public Task SelectPurposeAsync(string purpose)
    {
        // Scope to the Purpose field group specifically — .card-body holds the Terminal Outcome
        // combobox too, and only this .mb-3 block mentions "Purpose".
        var scope = page.Locator(".card-body .mb-3").Filter(new() { HasText = "Purpose" });
        return DropDownSelector.SelectAsync(page, scope, purpose);
    }

    /// <summary>
    /// True when the Purpose field is rendered on the edit form. It is hidden entirely for a
    /// terminal stage (Terminal Outcome = Hired/Rejected).
    /// </summary>
    public Task<bool> IsPurposeFieldVisibleAsync() =>
        page.Locator("label.form-label").Filter(new() { HasText = "Purpose" }).First.IsVisibleAsync();

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/recruitment-stages", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task<bool> HasErrorAsync() =>
        page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    public async Task<string?> GetErrorTextAsync() =>
        (await page.Locator(".alert-danger").First.TextContentAsync())?.Trim();

    /// <summary>
    /// Clicks Save expecting the server to reject it (e.g. duplicate name) — stays on the edit page
    /// and surfaces GlobalError, rather than navigating back to the list like <see cref="SaveAsync"/>.
    /// </summary>
    public async Task ClickSaveExpectingErrorAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForSelectorAsync(".alert-danger", new() { Timeout = 15_000 });
    }
}
