using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class GettingStartedPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".onboarding-progress, .alert-danger";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/getting-started");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public Task WaitForLoadAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    private ILocator TaskCard(string taskNameFragment) =>
        page.Locator(".card").Filter(new() { HasText = taskNameFragment }).First;

    public Task<bool> HasTaskAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).IsVisibleAsync();

    public Task<int> GetTaskCardCountAsync() =>
        page.Locator(".row.g-3 > .col-md-4 > .card").CountAsync();

    public Task<bool> IsTaskCompletedAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).GetByText("Completed", new() { Exact = true }).IsVisibleAsync();

    public async Task<string?> GetTaskLinkUrlAsync(string taskNameFragment)
    {
        var link = TaskCard(taskNameFragment).GetByRole(AriaRole.Link);
        return await link.IsVisibleAsync() ? await link.GetAttributeAsync("href") : null;
    }

    public Task ClickTaskLinkAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).GetByRole(AriaRole.Link).ClickAsync();

    public async Task<int> GetCompletionPercentageAsync()
    {
        var text = (await page.Locator(".onboarding-progress-label").TextContentAsync())?.Trim() ?? "0% complete";
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public async Task SkipForNowAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Skip for now" }).ClickAsync();
        await page.WaitForSelectorAsync(LoadedSelector,
            new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });
    }
}
