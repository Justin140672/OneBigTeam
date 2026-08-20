using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Getting Started" onboarding checklist (/getting-started —
/// GettingStarted.razor). Gated to HR Administrator / Company Administrator; anyone else is
/// redirected to Session.MyProfileUrl before any of this page's markup renders.
/// </summary>
public sealed class GettingStartedPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".onboarding-progress, .alert-danger";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/getting-started");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Waits for the checklist to finish loading after arriving here via redirect (e.g. landing
    /// on "/" and being bounced here by AppSession.LandingUrl) rather than a direct GoToAsync.
    /// </summary>
    public Task WaitForLoadAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    /// <summary>
    /// Each task is rendered as a Bootstrap ".card" (OnboardingTaskCard.razor) — there is no
    /// dedicated CSS class distinguishing one task card from another or from any other ".card"
    /// on the page, so every lookup here is scoped by the task's own Name text via Filter,
    /// mirroring ReportCatalogPage's Card(nameFragment) convention.
    /// </summary>
    private ILocator TaskCard(string taskNameFragment) =>
        page.Locator(".card").Filter(new() { HasText = taskNameFragment }).First;

    public Task<bool> HasTaskAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).IsVisibleAsync();

    public Task<int> GetTaskCardCountAsync() =>
        page.Locator(".row.g-3 > .col-md-4 > .card").CountAsync();

    /// <summary>
    /// True once the card shows the green "Completed" badge (OnboardingTaskCard.razor renders
    /// this instead of a "Go to task" link once Task.IsCompleted is true).
    /// </summary>
    public Task<bool> IsTaskCompletedAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).GetByText("Completed", new() { Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Returns the href of the task's action link (usually "Go to task", but e.g. "Download" for
    /// the import-template task — see OnboardingTaskCard.razor.ActionLabel), or null if the task
    /// is already completed (no link is rendered in that case). Matched by role rather than name
    /// since each card renders exactly one such link.
    /// </summary>
    public async Task<string?> GetTaskLinkUrlAsync(string taskNameFragment)
    {
        var link = TaskCard(taskNameFragment).GetByRole(AriaRole.Link);
        return await link.IsVisibleAsync() ? await link.GetAttributeAsync("href") : null;
    }

    public Task ClickTaskLinkAsync(string taskNameFragment) =>
        TaskCard(taskNameFragment).GetByRole(AriaRole.Link).ClickAsync();

    /// <summary>
    /// Reads the "NN% complete" label next to the progress bar (GettingStarted.razor's
    /// ".onboarding-progress-label").
    /// </summary>
    public async Task<int> GetCompletionPercentageAsync()
    {
        var text = (await page.Locator(".onboarding-progress-label").TextContentAsync())?.Trim() ?? "0% complete";
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public async Task SkipForNowAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Skip for now" }).ClickAsync();
        // Skip navigates away (to /dashboard/hr or the Company edit page) once dismissal
        // completes server-side — wait for the checklist itself to leave the DOM rather than
        // a specific URL, since the destination differs by persona (see GettingStarted.razor).
        await page.WaitForSelectorAsync(LoadedSelector,
            new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });
    }
}
