using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeOffboardingTab(IPage page)
{
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leaving");
        await page.WaitForSelectorAsync(
            "#leaving-checklist-section .progress, #leaving-checklist-section .hr-empty-state",
            new() { Timeout = 15_000 });
    }

    private ILocator Section => page.Locator("#leaving-checklist-section");


    public Task<bool> HasProgressPanelAsync() => Section.Locator(".progress").IsVisibleAsync();

    public Task<bool> HasChecklistCardAsync() =>
        Section.Locator(".card-header:has-text('Offboarding Checklist')").IsVisibleAsync();

    public async Task<int> GetProgressPercentAsync()
    {
        var bar = Section.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    private ILocator ChecklistCard => Section.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;

    private ILocator RowFor(string taskTitleFragment) =>
        ChecklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;

    public async Task<string?> GetChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var badge = RowFor(taskTitleFragment).Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public Task ExpectChecklistTaskStatusAsync(string taskTitleFragment, string status) =>
        Assertions.Expect(RowFor(taskTitleFragment).Locator(".badge").First)
            .ToHaveTextAsync(status, new() { Timeout = 15_000 });

    public Task<bool> HasChecklistTaskAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).WaitUntilVisibleAsync();

    public async Task<string?> GetChecklistTaskOwnerAsync(string taskTitleFragment) =>
        (await RowFor(taskTitleFragment).Locator("td").Nth(2).TextContentAsync())?.Trim();

    public async Task<string?> GetChecklistTaskDueDateAsync(string taskTitleFragment) =>
        (await RowFor(taskTitleFragment).Locator("td").Nth(3).TextContentAsync())?.Trim();

    public async Task<string?> GetChecklistTaskWaiveReasonAsync(string taskTitleFragment)
    {
        var caption = RowFor(taskTitleFragment).Locator(".text-muted.small").Filter(new() { HasText = "Waived:" }).First;
        return await caption.IsVisibleAsync() ? (await caption.TextContentAsync())?.Trim() : null;
    }

    public Task<bool> HasOpenTaskButtonAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Open task" }).IsVisibleAsync();

    public Task ClickOpenTaskAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Open task" }).ClickAsync();

    public Task<bool> HasWaiveButtonAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Waive" }).IsVisibleAsync();

    public async Task ClickWaiveAsync(string taskTitleFragment)
    {
        await RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Waive" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Waive Obligation" })
            .WaitForAsync(new() { Timeout = 8_000 });
    }
}
