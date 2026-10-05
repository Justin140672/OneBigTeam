using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VacancyManagementTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task VacancyList_ShowsSeededVacancies()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected 'Senior Software Engineer' in the vacancy list");
        Assert.True(await vacancyList.HasVacancyAsync("HR Business Partner"),
            "Expected 'HR Business Partner' in the vacancy list");

        await vacancyList.ShowAllVacanciesAsync();
        Assert.True(await vacancyList.HasVacancyAsync("Product Designer"),
            "Expected 'Product Designer' in the vacancy list");
    }

    [Fact]
    public async Task CreateVacancy_AppearsInList()
    {
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // A fresh Position Profile is required here rather than the seeded "Senior Software
        // Engineer" — that profile already has a permanently-open vacancy in seed data (see
        // PositionProfileTestHelpers' remarks), which the "one live vacancy per position profile"
        // rule would otherwise reject a second vacancy against.
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();

        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the new vacancy '{vacancyTitle}' to appear in the list after creation");
    }

    [Fact]
    public async Task CreateVacancy_WithoutEmploymentType_ShowsRequiredError_AndDoesNotSave()
    {
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();

        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.ClickSaveButtonAsync();

        await Assertions.Expect(_page.Locator("#vacancy-employment-type-error"))
            .ToContainTextAsync("Employment type is required", new() { Timeout = 10_000 });
        Assert.Contains("/vacancies/new", _page.Url);

        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the vacancy '{vacancyTitle}' to appear in the list once an employment type was chosen");
    }

    /// <summary>
    /// Verifies the "Refactor Duplicate Vacancy Fields" story's renamed field labels render on the
    /// "Recruitment Advert Details" card. (This test previously also asserted a Location
    /// fallback-hint field, since removed entirely along with Vacancy.Location by a later
    /// correction — location is now shown only as a read-only value derived from the linked
    /// Position Profile.)
    /// </summary>
    [Fact]
    public async Task CreateVacancy_ShowsAdvertFieldLabels_WithNoOptionalSuffix()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        Assert.True(await vacancyDetail.HasAdvertTitleLabelAsync(),
            "Expected the 'Advert Title' label to render");
        Assert.True(await vacancyDetail.HasAdvertDescriptionLabelAsync(),
            "Expected the 'Advert Description' label to render");

        Assert.False(await vacancyDetail.HasOptionalSuffixAsync(),
            "Did not expect any '(optional)' suffix on the Recruitment Advert Details card's field labels");
    }

    [Fact]
    public async Task CreateVacancy_WithoutPositionProfile_ShowsValidationError()
    {
        var vacancyTitle = $"E2E NoProfile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/vacancies/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/vacancies/new", _page.Url);
        Assert.True(await vacancyDetail.HasErrorAsync(),
            "Expected a validation error when saving a vacancy with no Position Profile selected");
    }

    [Fact]
    public async Task CreateVacancy_WithoutAdvertTitle_UsesPositionProfileTitleAsEffectiveTitle()
    {
        var profileTitle = $"E2E NoAdvertTitle Profile {Guid.NewGuid().ToString("N")[..8]}";

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
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        // Create a vacancy linked to that profile, deliberately leaving Advert Title blank.
        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(profileTitle),
            $"Expected the new vacancy to appear in the list showing '{profileTitle}' (the linked " +
            "Position Profile's title) as its effective title");

        await vacancyList.ClickVacancyAsync(profileTitle);

        Assert.Equal(profileTitle, await vacancyDetail.GetHeaderTextAsync());

        Assert.Equal(string.Empty, await vacancyDetail.GetTitleAsync());
    }

    /// <summary>
    /// Position Profile is only locked once UpdateVacancyHandler.CanChangePositionProfile's
    /// baseline check fails — Status is no longer Draft, or the vacancy has at least one
    /// application (confirmed against production behavior: a freshly-created Draft vacancy with
    /// zero applications is deliberately still editable, so this test must first give the vacancy
    /// an application before the dropdown will actually render disabled). Once locked, the
    /// dropdown must still show which profile it's linked to, just disabled, while the rest of the
    /// Overview form (Advert Title, Advert Description, Location, Hiring Manager) stays fully
    /// editable.
    /// </summary>
    [Fact]
    public async Task EditVacancy_PositionProfileIsDisabled_OtherFieldsRemainEditable()
    {
        var unique          = Guid.NewGuid().ToString("N")[..8];
        var vacancyTitle    = $"E2E Edit {unique}";
        var candidateLast   = $"LockCand{unique}";
        var candidateEmail  = $"e2e.lockcand{unique}@example.com";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        // A fresh Position Profile is required here rather than the seeded "Senior Software
        // Engineer" — that profile already has a permanently-open vacancy in seed data (see
        // PositionProfileTestHelpers' remarks), which the "one live vacancy per position profile"
        // rule would otherwise reject a second vacancy against.
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateLast);
        await vacancyDetail.SubmitAddApplicationAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be disabled once the vacancy has an application");
        Assert.StartsWith(profileTitle, await vacancyDetail.GetSelectedPositionProfileTextAsync());

        Assert.True(await vacancyDetail.HasRecruitmentAdvertDetailsHeaderAsync(),
            "Expected the vacancy's own details card to be headed 'Recruitment Advert Details'");

        Assert.Equal(0, await vacancyDetail.CountDepartmentFieldsInAdvertDetailsCardAsync());

        var updatedTitle = $"{vacancyTitle} Updated";
        await vacancyDetail.FillTitleAsync(updatedTitle);
        Assert.Equal(updatedTitle, await vacancyDetail.GetTitleAsync());

        await vacancyDetail.FillDescriptionAsync("Updated by E2E test");

        await vacancyDetail.SelectHiringManagerAsync("Laura");
        Assert.Contains("laura", await vacancyDetail.GetSelectedHiringManagerTextAsync() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // NOTE: EditVacancy_ClearingLocationOverride_ShowsBlank_NotAnAutoResolvedFallback used to live
    // here, testing setting/clearing a vacancy-level Location override. That field was removed
    // entirely (domain, API, UI) as part of the "Vacancy - Position Profile relationship" epic's
    // location correction — location is now shown only as a read-only value derived from the
    // linked Position Profile, with nothing to set or clear.

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromVacanciesPage()
    {
        const string tomEmail = "tom.williams@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(tomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/vacancies");
        await WaitForUrlToStopContainingAsync("/vacancies");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/vacancies"),
            $"Expected a plain employee to be redirected away from the vacancies page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task HrAdministrator_IsRedirectedAway_FromVacanciesPage()
    {
        const string lauraEmail = "laura.bennett@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(lauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/vacancies");
        await WaitForUrlToStopContainingAsync("/vacancies");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/vacancies"),
            $"Expected an HR Administrator without the Recruiter role to be redirected away from the vacancies page, but ended up at: {finalUrl}");
    }
}
