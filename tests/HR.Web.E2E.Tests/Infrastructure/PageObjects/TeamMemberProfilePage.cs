using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class TeamMemberProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/team-view");
        await WaitForSettledAsync();
    }

    public async Task WaitForSettledAsync() =>
        await page.Locator(
            "[data-testid='team-view-profile'], [data-testid='team-view-forbidden'], " +
            "[data-testid='team-view-not-found'], [data-testid='team-view-error']")
            .First.WaitForAsync(new() { Timeout = 20_000 });

    public ILocator NameHeading => page.Locator("[data-testid='team-view-name']");
    public ILocator JobTitle => page.Locator("[data-testid='team-view-job-title']");
    public ILocator ForbiddenAlert => page.Locator("[data-testid='team-view-forbidden']");
    public ILocator NotFoundAlert => page.Locator("[data-testid='team-view-not-found']");

    public Task<bool> IsProfileVisibleAsync() => page.Locator("[data-testid='team-view-profile']").IsVisibleAsync();
    public Task<bool> IsForbiddenAsync() => ForbiddenAlert.IsVisibleAsync();
    public Task<bool> IsNotFoundAsync() => NotFoundAlert.IsVisibleAsync();

    public async Task<string> GetDisplayNameAsync() => (await NameHeading.TextContentAsync())?.Trim() ?? "";

    public Task<string> GetPageTextAsync() => page.ContentAsync();
}
