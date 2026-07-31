using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Update Vacancy Details and List Screens" story's UI changes:
/// - Vacancy list: the Title column shows a muted "(from Position Profile)" fallback indicator
///   when the vacancy's own AdvertTitle is null (VacancyList.razor). The Location column has no
///   such indicator — Vacancy.Location was removed entirely by a later correction, so Location is
///   now unconditionally just the linked Position Profile's location.
/// - Vacancy detail: a "View Position Profile" link on the "Linked Position Profile" card
///   navigates to the linked profile's own view page.
/// - Vacancy detail: the Position Profile dropdown is editable for a Draft vacancy with zero
///   applications, and locked (with an explanatory inline message) once that's no longer true.
///
/// Coverage here is deliberately additive to VacancyLinkedPositionProfileTests (which already
/// covers the read-only card's Title/Department/Description sourcing and the list's "Position
/// Profile" column) and VacancyPositionProfileDefaultsTests (which covers the create-form
/// defaults summary card) — see those files' header comments. Nothing here duplicates those.
///
/// Uses Marcus Diallo (Recruiter) for vacancy/candidate actions (recruitment:manage is
/// Recruiter-only) and Laura Bennett (HR Administrator) for position profile creation
/// (infra:manage), switching accounts via LoginPage.SwitchAccountAsync as needed — same pattern
/// as the sibling test files above.
/// </summary>
[Collection("E2E")]
public sealed class VacancyDetailsAndListScreensUpdateTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task VacancyList_TitleColumn_ShowsFallbackIndicatorOnlyWhenAdvertTitleIsUnset()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var withOverrideTitle = $"E2E Explicit {Guid.NewGuid().ToString("N")[..8]}";

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Vacancy with an explicit AdvertTitle override — no fallback indicator expected.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(withOverrideTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(withOverrideTitle));
        Assert.False(await vacancyList.HasTitleColumnPositionProfileFallbackIndicatorAsync(withOverrideTitle),
            "Did not expect the '(from Position Profile)' indicator for a vacancy with its own AdvertTitle set");

        // "Senior Software Engineer" is the seeded vacancy with no AdvertTitle of its own — per
        // RecruitmentModule.SeedRecruitmentAsync's own comment, it's an exact title match to its
        // linked Position Profile, deliberately left with a null AdvertTitle to exercise the
        // fallback path. "HR Business Partner"/"Product Designer" are NOT fallback cases — the
        // same seed method deliberately gives them their own distinct AdvertTitle, since their
        // linked Position Profile's title genuinely differs (no "HR Business Partner"/"Product
        // Designer" profile exists) — so they never show this indicator.
        Assert.True(await vacancyList.HasTitleColumnPositionProfileFallbackIndicatorAsync("Senior Software Engineer"),
            "Expected the '(from Position Profile)' indicator for the seeded vacancy with no AdvertTitle override");
    }

    // NOTE: VacancyList_LocationColumn_ShowsFallbackIndicatorOnlyWhenLocationIsUnset used to live
    // here. Vacancy.Location was removed entirely (domain, API, UI) as part of the "Vacancy -
    // Position Profile relationship" epic's location correction — the Location column now
    // unconditionally shows the linked Position Profile's location with no override-vs-fallback
    // distinction to test, so this test's premise no longer holds and it was removed along with
    // VacancyListPage.HasLocationColumnPositionProfileFallbackIndicatorAsync.

    [Fact]
    public async Task ViewingVacancy_ViewPositionProfileLink_NavigatesToLinkedPositionProfile()
    {
        var profileTitle = $"E2E Nav Profile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        // Department, Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(profileTitle);

        Assert.True(await vacancyDetail.IsViewPositionProfileLinkVisibleAsync(),
            "Expected the 'View Position Profile' link to render for a vacancy with a linked Position Profile");

        await vacancyDetail.ClickViewPositionProfileLinkAsync();
        await _page.WaitForURLAsync("**/position-profiles/**/view", new() { Timeout = 15_000 });

        Assert.Contains($"/companies/{AcmeId}/position-profiles/", _page.Url);
        Assert.EndsWith("/view", _page.Url);
        Assert.Equal(profileTitle, await ppEdit.GetTitleAsync());
    }

    [Fact]
    public async Task NewDraftVacancyWithNoApplications_PositionProfileDropdown_IsEditableAndSavesSuccessfully()
    {
        var initialProfileTitle = $"E2E Initial Profile {Guid.NewGuid().ToString("N")[..8]}";
        var newProfileTitle     = $"E2E Retarget Profile {Guid.NewGuid().ToString("N")[..8]}";
        var vacancyTitle        = $"E2E Retarget Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(initialProfileTitle);
        // Department, Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(newProfileTitle);
        await ppEdit.SelectDepartmentAsync("Sales");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await login.SwitchAccountAsync(MarcusEmail);

        // New vacancies start life as Draft with zero applications, so this vacancy is eligible
        // for a Position Profile change immediately after creation — no extra setup needed.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(initialProfileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.False(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be editable for a Draft vacancy with zero applications");
        Assert.False(await vacancyDetail.IsPositionProfileLockedMessageVisibleAsync(),
            "Did not expect the locked-explanation message while the dropdown is still editable");

        await vacancyDetail.SelectPositionProfileAsync(newProfileTitle);
        await vacancyDetail.SaveExistingVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.Equal(newProfileTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
    }

    [Fact]
    public async Task VacancyWithApplication_PositionProfileDropdown_IsDisabledWithExplanatoryMessage()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Lock{unique}";
        var candidateName = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.lock{unique}@example.com";
        var vacancyTitle   = $"E2E Locked Vacancy {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        // Adding an application (even while the vacancy stays Draft) is enough to make
        // UpdateVacancyHandler.CanChangePositionProfile false — the eligibility rule is
        // "status == Draft && applicationCount == 0", not status alone.
        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        await vacancyDetail.GoToAsync(AcmeId, await GetVacancyIdFromUrlAsync());

        Assert.True(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be disabled once the vacancy has an application");
        Assert.True(await vacancyDetail.IsPositionProfileLockedMessageVisibleAsync(),
            "Expected the explanatory inline message once the Position Profile dropdown is locked");
    }

    // NOTE: VacancyDetail_LocationField_ShowsFallbackOrOverrideHintAppropriately used to live
    // here, testing the vacancy-level Location override's contextual hint. That field was removed
    // entirely (domain, API, UI) as part of the "Vacancy - Position Profile relationship" epic's
    // location correction — location is now shown only as a read-only value derived from the
    // linked Position Profile, with nothing to override and no hint to test.

    private async Task<Guid> GetVacancyIdFromUrlAsync()
    {
        // After adding an application, the Applications tab keeps the same route
        // (/vacancies/{id}) — extract the id straight from the current URL rather than
        // navigating away and back through the list, since the vacancy's list-row title now
        // resolves via EffectiveTitle and could theoretically collide with search behavior.
        var uri = new Uri(_page.Url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var vacanciesIndex = Array.IndexOf(segments, "vacancies");
        return Guid.Parse(segments[vacanciesIndex + 1]);
    }
}
