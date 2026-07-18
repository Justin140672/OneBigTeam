using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Update Create Vacancy Workflow" story's Add Vacancy form behavior:
/// - Selecting a Position Profile shows a "From Position Profile" summary card reflecting the
///   selected profile's Department/Salary Range.
/// - The vacancy-level Department dropdown that used to also live on this form was removed
///   entirely by the later "Refactor Duplicate Vacancy Fields" story — department is now shown
///   only via that read-only summary card (during create) and the "Linked Position Profile" card
///   (once the vacancy exists); see CreateVacancy_DepartmentFieldIsAbsentFromAdvertDetailsCard.
///
/// Position profile creation (recruitment infra:manage via Session.CanManageEmployees) and vacancy
/// creation (recruitment:manage, Recruiter-only) are gated to different roles — see
/// PositionProfileManagementTests and VacancyManagementTests' header comments respectively — so
/// tests here switch between Laura Bennett (HR Administrator, position profiles) and Marcus Diallo
/// (Recruiter, vacancies) via LoginPage.SwitchAccountAsync.
/// </summary>
[Collection("E2E")]
public sealed class VacancyPositionProfileDefaultsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task SelectingPositionProfile_ShowsDefaultsSummaryCard()
    {
        var profileTitle = $"E2E Profile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        // Create a fresh Position Profile with a known Department and Salary Range as Laura
        // (HR Administrator) so the assertions below don't depend on hardcoded seed values.
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        // Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.FillSalaryRangeAsync(50000, 70000);
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        // Switch to Marcus (Recruiter) to create the vacancy — recruitment:manage is Recruiter-only.
        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);

        Assert.True(await vacancyDetail.IsPositionProfileDefaultsSummaryVisibleAsync(),
            "Expected the 'From Position Profile' summary card to appear once a profile is selected");

        Assert.Contains("Engineering", await vacancyDetail.GetSummaryDepartmentNameAsync() ?? string.Empty);
        Assert.Contains("50,000", await vacancyDetail.GetSummarySalaryRangeAsync() ?? string.Empty);

        // The vacancy-level Department dropdown that used to also be asserted here (auto-populated
        // and disabled once a Position Profile was selected) was removed entirely by the "Refactor
        // Duplicate Vacancy Fields" story — department is now shown only via the "From Position
        // Profile" summary card asserted above (during create) and the read-only "Linked Position
        // Profile" card (once the vacancy exists). See
        // CreateVacancy_DepartmentFieldIsAbsentFromAdvertDetailsCard for coverage of its removal.
    }

    /// <summary>
    /// The vacancy-level Department dropdown was removed entirely from the "Recruitment Advert
    /// Details" card by the "Refactor Duplicate Vacancy Fields" story — department is now derived
    /// solely from the selected Position Profile and shown read-only via the "From Position
    /// Profile" summary card / "Linked Position Profile" card. This replaces the prior
    /// "CreateVacancy_DepartmentDropdownIsDisabled" test, whose premise (a disabled-but-present
    /// Department dropdown) no longer holds.
    /// </summary>
    [Fact]
    public async Task CreateVacancy_DepartmentFieldIsAbsentFromAdvertDetailsCard()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        Assert.Equal(0, await vacancyDetail.CountDepartmentFieldsInAdvertDetailsCardAsync());
    }

    /// <summary>
    /// Intended to verify that only active Position Profiles appear in the dropdown's options when
    /// creating a vacancy (the dropdown's DataSource is active-only — see
    /// PositionProfileService.ListPositionProfilesAsync's default includeInactive: false, used by
    /// VacancyDetail.razor's OnLoadedAsync).
    ///
    /// This cannot currently be exercised end-to-end from this test project: there is no product
    /// feature (UI action or API endpoint) to deactivate a Position Profile.
    /// PositionProfile.Deactivate(DateTimeOffset) exists on the domain entity
    /// (HR.Modules.Employees.Domain.PositionProfile) but nothing calls it — compare
    /// DeactivateDepartment/DeactivateEmploymentType/DeactivateLocation, which all have a
    /// Features/Deactivate*/Endpoint.cs + Handler.cs pair, and their *List.razor pages register a
    /// "Deactivate" toolbar action via ConfigureToolbar(). PositionProfileList.razor never overrides
    /// ConfigureToolbar(), so there's no "Deactivate" action in that grid either. Every Position
    /// Profile reachable from the UI (seeded or created by this test suite) therefore always has
    /// IsActive == true, so a genuinely inactive profile can't be produced without adding backend/UI
    /// production code, which is out of scope for this test-only change.
    ///
    /// Skipped (rather than faked with a weaker assertion) so this gap stays visible. Un-skip and
    /// fill in the body once a "Deactivate Position Profile" feature ships: seed/create one active
    /// and one inactive profile for the same company, open this dropdown on the vacancy create
    /// form, and assert only the active profile's title is among GetPositionProfileDropdownOptionsAsync().
    /// </summary>
    [Fact(Skip = "Blocked: no product feature exists yet to deactivate a Position Profile (no UI action, no API endpoint) — see doc comment on this test.")]
    public async Task CreateVacancy_PositionProfileDropdown_OnlyShowsActiveProfiles()
    {
        await Task.CompletedTask;
    }
}
