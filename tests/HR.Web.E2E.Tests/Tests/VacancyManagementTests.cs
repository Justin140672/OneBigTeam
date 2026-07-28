using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies Recruiter CRUD workflows for vacancies:
/// - Seeded vacancies appear in the list.
/// - A new vacancy can be created and appears in the list.
/// - Validation errors surface when required fields are missing.
/// - Plain employees and HR Administrators (who lack the Recruiter role) cannot reach the
///   vacancies page.
///
/// Uses Marcus Diallo (Recruiter role) rather than Laura Bennett (HR Administrator) —
/// recruitment:manage (vacancy creation) is Recruiter-only (see IdentityModule.AddRolePolicies);
/// an HR Administrator does not automatically get recruitment access. recruitment:view (reading
/// vacancies) stays broader at the API layer and would still let Laura read vacancy data
/// directly, but the /vacancies workspace page itself is gated to Session.IsRecruiter (see
/// VacancyList.razor OnBeforeLoadAsync), so she's still redirected away from it in the UI —
/// this file exercises the write path throughout, so Marcus is used for the CRUD tests.
/// </summary>
[Collection("E2E")]
public sealed class VacancyManagementTests(AppFixture fixture) : E2ETestBase(fixture)
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

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();

        await vacancyDetail.FillTitleAsync(vacancyTitle);
        // Position Profile is now mandatory for vacancy creation (the API rejects a vacancy with
        // no PositionProfileId belonging to the same company) — "Senior Software Engineer" is
        // seeded for Acme (see EmployeesModule.SeedEmployeesAsync).
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the new vacancy '{vacancyTitle}' to appear in the list after creation");
    }

    /// <summary>
    /// Verifies the "Refactor Duplicate Vacancy Fields" story's renamed field labels render on the
    /// "Recruitment Advert Details" card. (This test previously also asserted a Location
    /// fallback-hint field, since removed entirely along with Vacancy.Location by a later
    /// correction — location is now shown only as a read-only value derived from the linked
    /// Position Profile.)
    /// </summary>
    [Fact]
    public async Task CreateVacancy_ShowsRenamedOptionalAdvertFieldLabels()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        Assert.True(await vacancyDetail.HasAdvertTitleLabelAsync(),
            "Expected the 'Advert Title (optional)' label to render");
        Assert.True(await vacancyDetail.HasAdvertDescriptionLabelAsync(),
            "Expected the 'Advert Description (optional)' label to render");
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

        // Fill every required field except Position Profile, then try to save.
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/vacancies/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/vacancies/new", _page.Url);
        Assert.True(await vacancyDetail.HasErrorAsync(),
            "Expected a validation error when saving a vacancy with no Position Profile selected");
    }

    /// <summary>
    /// As part of the "Refactor Duplicate Vacancy Fields" story, the Advert Title field was
    /// renamed to "Advert Title (optional)" and is genuinely no longer required — it was
    /// previously mandatory (this test used to assert a validation error on an empty title). A
    /// vacancy can now be created with just a Position Profile and no Advert Title at all, in
    /// which case "EffectiveTitle" (used everywhere a resolved title is displayed — the list's
    /// Title column and this page's own header) falls back to the linked Position Profile's title.
    /// </summary>
    [Fact]
    public async Task CreateVacancy_WithoutAdvertTitle_UsesPositionProfileTitleAsEffectiveTitle()
    {
        var profileTitle = $"E2E NoAdvertTitle Profile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        // Seed a Position Profile with a unique title so the assertions below can't collide with
        // any other seeded/created vacancy's title.
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

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        // Create a vacancy linked to that profile, deliberately leaving Advert Title blank.
        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        // The list's Title column shows the resolved EffectiveTitle — the linked Position
        // Profile's title, since no Advert Title was set.
        Assert.True(await vacancyList.HasVacancyAsync(profileTitle),
            $"Expected the new vacancy to appear in the list showing '{profileTitle}' (the linked " +
            "Position Profile's title) as its effective title");

        await vacancyList.ClickVacancyAsync(profileTitle);

        // The detail page's header also shows the resolved EffectiveTitle.
        Assert.Equal(profileTitle, await vacancyDetail.GetHeaderTextAsync());

        // The raw Advert Title field itself is genuinely empty — EffectiveTitle is a fallback for
        // display purposes only, not a value silently written back into AdvertTitle.
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

        // Create a vacancy with a known Position Profile so its value can be asserted after reopening.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        // Give the vacancy an application — CanChangePositionProfile requires both Draft status
        // AND zero applications, so a bare Draft vacancy alone is not enough to lock this field.
        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateEmail);
        await vacancyDetail.SubmitAddApplicationAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        // Position Profile still shows the value it was created with, but can no longer be changed.
        Assert.True(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be disabled once the vacancy has an application");
        Assert.Equal("Senior Software Engineer", await vacancyDetail.GetSelectedPositionProfileTextAsync());

        // The vacancy's own fields card was renamed from "Vacancy Details" to "Recruitment Advert
        // Details" as part of the "Derive Vacancy Role Information from Position Profile" story —
        // confirm the new header renders; the edit behavior underneath it is unchanged and
        // exercised by the assertions below.
        Assert.True(await vacancyDetail.HasRecruitmentAdvertDetailsHeaderAsync(),
            "Expected the vacancy's own details card to be headed 'Recruitment Advert Details'");

        // The vacancy-level Department dropdown that used to also be asserted here was removed
        // entirely by the "Refactor Duplicate Vacancy Fields" story. Asserted here (rather than
        // only on the create form) specifically because this is edit mode on an existing vacancy,
        // where the separate, unrelated "Linked Position Profile" card also renders a read-only
        // Department <dt>/<dd> pair — proving the "Recruitment Advert Details" card scoping
        // doesn't accidentally pick that up as a false negative.
        Assert.Equal(0, await vacancyDetail.CountDepartmentFieldsInAdvertDetailsCardAsync());

        // Advert Title, Advert Description and Location all remain editable.
        var updatedTitle = $"{vacancyTitle} Updated";
        await vacancyDetail.FillTitleAsync(updatedTitle);
        Assert.Equal(updatedTitle, await vacancyDetail.GetTitleAsync());

        await vacancyDetail.FillDescriptionAsync("Updated by E2E test");

        // Hiring Manager's Text field is WorkEmail (lowercase, e.g. "laura.bennett@acme.example")
        // even though the dropdown's ItemTemplate displays "Laura Bennett" while the list is open —
        // compare case-insensitively rather than assuming either casing for the committed value.
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
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/vacancies"),
            $"Expected a plain employee to be redirected away from the vacancies page, but ended up at: {finalUrl}");
    }

    // HR Administrator (Laura) no longer holds the Recruiter role, so the vacancies list page
    // guard (VacancyList.razor OnBeforeLoadAsync) redirects her away the same as a plain
    // employee — even though the underlying recruitment:view API policy still lets her read
    // vacancy data (see HrAdministrator_Gets_Ok_Listing_Vacancies in RecruitmentAuthorizationTests).
    // The dedicated /vacancies workspace is Recruiter-only at the UI level by product decision.
    [Fact]
    public async Task HrAdministrator_IsRedirectedAway_FromVacanciesPage()
    {
        const string lauraEmail = "laura.bennett@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(lauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/vacancies");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/vacancies"),
            $"Expected an HR Administrator without the Recruiter role to be redirected away from the vacancies page, but ended up at: {finalUrl}");
    }
}
