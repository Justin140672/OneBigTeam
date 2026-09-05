using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Creates a fresh, uniquely-titled Position Profile via the UI for tests that need to create a
/// NEW vacancy. Tests can no longer reuse the seeded "Senior Software Engineer" or "HR Advisor"
/// profiles for this purpose: RecruitmentModule's seed data permanently attaches a never-closed
/// vacancy to each of them (an Open one for "Senior Software Engineer", a Draft one for
/// "HR Advisor" via the "HR Business Partner" vacancy) — see CreateVacancyHandler's "one live
/// vacancy per position profile" rule, which those seeded vacancies would otherwise immediately
/// violate for any test attempting to open a second vacancy against the same profile.
/// </summary>
internal static class PositionProfileTestHelpers
{
    /// <summary>
    /// Logs into <paramref name="hrAdminEmail"/> (Position Profile creation requires
    /// "employee:manage", which Recruiter-only personas like Marcus Diallo do not hold), creates a
    /// uniquely-titled, fully-valid Position Profile, then switches back to
    /// <paramref name="returnToEmail"/> so the caller can continue as whichever persona it was
    /// using before (typically the Recruiter creating the vacancy). Returns the new profile's title.
    /// </summary>
    public static async Task<string> CreateUniquePositionProfileAsync(
        IPage page,
        string webBaseUrl,
        Guid companyId,
        LoginPage login,
        string hrAdminEmail,
        string returnToEmail,
        string titlePrefix = "E2E Vacancy Profile")
    {
        var title = $"{titlePrefix} {Guid.NewGuid():N}"[..Math.Min(60, titlePrefix.Length + 33)];

        var ppList = new PositionProfileListPage(page, webBaseUrl);
        var ppEdit = new PositionProfileEditPage(page, webBaseUrl);

        await login.SwitchAccountAsync(hrAdminEmail);

        await ppList.GoToAsync(companyId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(title);
        // Department, Location and Default Leave Policy are mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await login.SwitchAccountAsync(returnToEmail);

        return title;
    }
}
