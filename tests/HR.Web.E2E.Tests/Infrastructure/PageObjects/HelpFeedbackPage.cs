using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class HelpFeedbackPage(IPage page, string baseUrl)
{
    private ILocator SubmissionForm => page.Locator(".card").Filter(new() { HasText = "New Submission" });
    private ILocator SubmissionsCard => page.Locator(".card").Filter(new() { HasText = "My Submissions" });

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/support");
        await page.WaitForSelectorAsync("button:has-text('Submit')", new() { Timeout = 20_000 });
    }

    public Task SelectTypeAsync(string type) =>
        DropDownSelector.SelectAsync(page, SubmissionForm, type, index: 0);

    public Task SelectPriorityAsync(string priority) =>
        DropDownSelector.SelectAsync(page, SubmissionForm, priority, index: 1);

    public async Task FillTitleAsync(string title)
    {
        await SubmissionForm.GetByPlaceholder("Short summary").FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDescriptionAsync(string description)
    {
        await SubmissionForm.GetByPlaceholder("Please describe the problem, feature, or question in detail").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<bool> IsIncludeDiagnosticsCheckedAsync() =>
        page.Locator("#includeDiagnostics").IsCheckedAsync();

    public Task SetIncludeDiagnosticsAsync(bool value) =>
        page.Locator("#includeDiagnostics").SetCheckedAsync(value);

    public async Task SubmitAsync()
    {
        await SubmissionForm.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await page.WaitForURLAsync("**/support/*", new() { Timeout = 20_000 });
    }

    public Task<bool> HasErrorAsync() =>
        SubmissionForm.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    private const string RowsRenderedSelector = ".list-group-item, p:has-text('No submissions yet.')";

    public async Task WaitForSubmissionsLoadedAsync() =>
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

    public async Task<bool> HasSubmissionAsync(string titleFragment)
    {
        await WaitForSubmissionsLoadedAsync();
        return await SubmissionsCard.Locator(".list-group-item")
            .Filter(new() { HasText = titleFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public async Task OpenSubmissionAsync(string titleFragment)
    {
        await WaitForSubmissionsLoadedAsync();
        await SubmissionsCard.Locator(".list-group-item")
            .Filter(new() { HasText = titleFragment })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync("h1", new() { Timeout = 20_000 });
    }

    public Task SelectStatusFilterAsync(string status) =>
        DropDownSelector.SelectAsync(page, SubmissionsCard.Locator(".support-status-filter"), status);
}
