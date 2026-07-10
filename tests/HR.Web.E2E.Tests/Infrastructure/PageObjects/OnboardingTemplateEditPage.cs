using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the onboarding template create/edit page.
/// Routes: /companies/{id}/onboarding-templates/new  and  /companies/{id}/onboarding-templates/{id}
/// </summary>
public sealed class OnboardingTemplateEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/onboarding-templates/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public Task FillNameAsync(string name) =>
        page.GetByPlaceholder("e.g. Standard Engineering Onboarding").FillAsync(name);

    // Scoped to .First: the template-level description field is always the first element in the
    // DOM with this placeholder — checklist task rows (added via ClickAddTaskAsync) reuse the same
    // "Optional description" placeholder further down the page.
    public Task FillDescriptionAsync(string description) =>
        page.GetByPlaceholder("Optional description").First.FillAsync(description);

    public Task ClickAddTaskAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Add Task" }).ClickAsync();

    public Task FillTaskTitleAsync(string title) =>
        page.GetByPlaceholder("Task title").First.FillAsync(title);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the onboarding-templates list on success.
        await page.WaitForURLAsync("**/onboarding-templates", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task<bool> HasErrorAsync() =>
        page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();
}
