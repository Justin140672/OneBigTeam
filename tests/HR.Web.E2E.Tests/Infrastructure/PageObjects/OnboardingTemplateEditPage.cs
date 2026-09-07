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

    public async Task GoToAsync(Guid companyId, Guid templateId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/onboarding-templates/{templateId}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Standard Engineering Onboarding").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetNameAsync() =>
        page.GetByPlaceholder("e.g. Standard Engineering Onboarding").InputValueAsync();

    public async Task<string> GetTaskTitleAsync()
    {
        await EnsureFirstTaskExpandedAsync();
        return await page.GetByPlaceholder("Task title").First.InputValueAsync();
    }

    // Scoped to .First: the template-level description field is always the first element in the
    // DOM with this placeholder — checklist task rows (added via ClickAddTaskAsync) reuse the same
    // "Optional description" placeholder further down the page.
    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").First.FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickAddTaskAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Add Task" }).ClickAsync();

    public async Task FillTaskTitleAsync(string title)
    {
        await EnsureFirstTaskExpandedAsync();
        await page.GetByPlaceholder("Task title").First.FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// A checklist task's Title/Description/Priority/Assign-To fields only render once its row is
    /// expanded (OnboardingTemplateEdit.razor's collapsible "task-disclosure" UI) — a freshly
    /// added task auto-expands (see AddTask()), but navigating to/reloading an EXISTING template
    /// starts with every task collapsed, so its "Task title" input isn't in the DOM at all until
    /// the row is clicked open. Expands the first task row if it isn't already, no-ops otherwise.
    /// </summary>
    private async Task EnsureFirstTaskExpandedAsync()
    {
        if (await page.GetByPlaceholder("Task title").CountAsync() > 0)
            return;

        var firstDisclosure = page.Locator(".task-disclosure").First;
        await firstDisclosure.WaitForAsync(new() { Timeout = 15_000 });

        // Navigating straight to an existing template's edit route (page.GotoAsync) reconnects a
        // fresh Blazor Server circuit — the disclosure button's DOM element can be visible (and
        // pass Playwright's actionability check) a moment before the circuit's own event
        // delegation has actually attached to it, the same render-vs-interop-ready gap documented
        // for Syncfusion comboboxes elsewhere in this suite (see DropDownSelector's remarks). A
        // click landing in that gap is silently swallowed: no exception, no expanded row, nothing
        // to retry on for a single ClickAsync + WaitForAsync pair. Hovering first (forcing a real
        // mouse move rather than a synthetic click at coordinates) and pausing gives that
        // OnAfterRenderAsync interop round trip a realistic chance to finish before the real click,
        // matching DropDownSelector's mitigation for the same class of race. Retry the click
        // itself, not just the wait, using "aria-expanded" (rather than the placeholder, which
        // won't exist until the panel has genuinely rendered) as the per-attempt signal that this
        // specific click landed.
        await firstDisclosure.HoverAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(350);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await firstDisclosure.ClickAsync(new() { Force = attempt == 5 });
            try
            {
                await page.Locator(".task-disclosure[aria-expanded='true']").First
                    .WaitForAsync(new() { Timeout = attempt < 5 ? 3_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 5)
            {
                // Click didn't register — pause to let interop catch up, then try again.
                await page.WaitForTimeoutAsync(300);
            }
        }

        await page.GetByPlaceholder("Task title").First.WaitForAsync(new() { Timeout = 10_000 });
    }

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
