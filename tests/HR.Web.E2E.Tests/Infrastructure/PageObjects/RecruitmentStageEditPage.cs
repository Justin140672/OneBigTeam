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
