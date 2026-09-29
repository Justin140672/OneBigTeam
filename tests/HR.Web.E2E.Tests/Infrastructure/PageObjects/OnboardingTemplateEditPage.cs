using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class OnboardingTemplateEditPage(IPage page, string baseUrl)
{
    private const string NamePlaceholder = "e.g. Standard Engineering Onboarding";
    private const string DescriptionPlaceholder = "Optional description";

    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/onboarding-templates/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task GoToEditAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/onboarding-templates/{id}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    private async Task FillTextBoxAsync(string placeholder, string value)
    {
        var input = page.GetByPlaceholder(placeholder);
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await input.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            if (value.Length > 0)
                await input.PressSequentiallyAsync(value, new() { Delay = 25 });
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(300);

            if (await input.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    public Task FillNameAsync(string name) => FillTextBoxAsync(NamePlaceholder, name);

    public Task<string> GetNameAsync() => page.GetByPlaceholder(NamePlaceholder).InputValueAsync();

    public Task SetDescriptionAsync(string value) => FillTextBoxAsync(DescriptionPlaceholder, value);

    public Task<string> GetDescriptionAsync() =>
        page.GetByPlaceholder(DescriptionPlaceholder).InputValueAsync();

    public async Task<string> WaitForDescriptionAsync(string expected)
    {
        var input = page.GetByPlaceholder(DescriptionPlaceholder);
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/onboarding-templates",
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(
            ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger", new() { Timeout = 30_000 });
    }

    // ── Optimistic-concurrency conflict banner (Ticket 2) — shared <SaveConflictBanner>. ──
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }


    public Task GoToAsync(Guid companyId, Guid templateId) => GoToEditAsync(companyId, templateId);

    public Task FillDescriptionAsync(string description) => SetDescriptionAsync(description);

    public Task ClickAddTaskAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Add Task" }).ClickAsync();

    public async Task FillTaskTitleAsync(string title)
    {
        await EnsureFirstTaskExpandedAsync();
        await page.GetByPlaceholder("Task title").First.FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetTaskTitleAsync()
    {
        await EnsureFirstTaskExpandedAsync();
        return await page.GetByPlaceholder("Task title").First.InputValueAsync();
    }

    public Task<bool> HasErrorAsync() =>
        page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    // ── Checklist helpers for the concurrency-conflict reload test (Ticket 2 follow-up). ──

    public async Task AddTaskWithTitleAsync(string title)
    {
        await ClickAddTaskAsync();
        await FillTaskTitleAsync(title);
    }

    public async Task<string> WaitForFirstTaskTitleAsync(string expected)
    {
        await EnsureFirstTaskExpandedAsync();
        var input = page.GetByPlaceholder("Task title").First;
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    private async Task EnsureFirstTaskExpandedAsync()
    {
        if (await page.GetByPlaceholder("Task title").CountAsync() > 0)
            return;

        var firstDisclosure = page.Locator(".task-disclosure").First;
        await firstDisclosure.WaitForAsync(new() { Timeout = 15_000 });
        await firstDisclosure.HoverAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(350);

        var taskTitleInput = page.GetByPlaceholder("Task title");
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            if (await taskTitleInput.CountAsync() > 0)
                break;

            await firstDisclosure.ClickAsync(new() { Force = attempt >= 6 });
            try
            {
                await taskTitleInput.First.WaitForAsync(new() { Timeout = attempt < 8 ? 3_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 8)
            {
                await page.WaitForTimeoutAsync(400);
            }
        }

        await page.GetByPlaceholder("Task title").First.WaitForAsync(new() { Timeout = 10_000 });
    }
}
