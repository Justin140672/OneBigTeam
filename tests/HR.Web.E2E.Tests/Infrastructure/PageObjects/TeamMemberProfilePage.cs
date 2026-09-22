using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Read-only, manager-facing employee profile
/// (src/HR.Web/Components/Pages/Employees/TeamMemberProfile.razor). Route:
/// /companies/{CompanyId:guid}/employees/{Id:guid}/team-view. Reachable via GetEmployeeTeamView
/// (manager hierarchy only — self and HR use the separate full GetEmployee/EmployeeEdit.razor
/// page instead). Renders only the fields GetEmployeeTeamViewResponse carries: no edit control,
/// "More actions" menu, notes, compensation, system-access, personal-details or leaving-process
/// administration exists on this page at all.
/// </summary>
public sealed class TeamMemberProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/team-view");
        await WaitForSettledAsync();
    }

    /// <summary>Waits for either the profile content or one of the failure states to render.</summary>
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

    /// <summary>
    /// Full page text content, for asserting the absence of restricted values (personal email,
    /// DOB, nationality, gender, home address, notice period, HR notes, concurrency token) — the
    /// manager team-view response has no property for any of these at all, so they can never
    /// appear here regardless of what the underlying employee record contains.
    /// </summary>
    public Task<string> GetPageTextAsync() => page.ContentAsync();
}
