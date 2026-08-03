using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Prevent Invalid Position Profile Changes" story's authorised-correction
/// affordance on the Vacancy detail page (data-testid="position-profile-correction-section" in
/// VacancyDetail.razor's RenderDetailsCard). Once a vacancy has received an application (or moved
/// past Draft status), its Position Profile dropdown is normally locked
/// (GetVacancyResponse.CanChangePositionProfile == false — see
/// UpdateVacancyHandler.CanChangePositionProfile). A Recruiter can now check "This is an
/// authorised correction", supply a Correction Reason, and change the Position Profile anyway;
/// see UpdateVacancyValidator/UpdateVacancyHandler for the server-side counterpart.
///
/// Uses Marcus Diallo (Recruiter role) throughout — recruitment:manage is Recruiter-only (see
/// IdentityModule.AddRolePolicies), matching the sibling Vacancy test files in this directory.
/// </summary>
[Collection("E2E")]
public sealed class VacancyPositionProfileCorrectionTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task LockedVacancy_ForRecruiter_ShowsDisabledDropdownAndCorrectionCheckbox()
    {
        var (_, vacancyDetail) = await ArrangeVacancyWithApplicationAsync();

        Assert.True(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be locked once the vacancy has an application");
        Assert.True(await vacancyDetail.IsPositionProfileLockedMessageVisibleAsync(),
            "Expected the locked-dropdown explanatory message to be shown");
        Assert.True(await vacancyDetail.IsCorrectionSectionVisibleAsync(),
            "Expected the authorised-correction section to render for a Recruiter on a locked vacancy");
        Assert.True(await vacancyDetail.IsCorrectionCheckboxVisibleAsync());
        Assert.False(await vacancyDetail.IsCorrectionCheckboxCheckedAsync());
        Assert.False(await vacancyDetail.IsCorrectionReasonFieldVisibleAsync(),
            "The Correction Reason field should not appear until the checkbox is checked");
    }

    [Fact]
    public async Task CheckingCorrectionCheckbox_RevealsReasonField_AndReenablesDropdown()
    {
        var (_, vacancyDetail) = await ArrangeVacancyWithApplicationAsync();

        await vacancyDetail.SetAuthorisedCorrectionCheckedAsync(true);

        Assert.True(await vacancyDetail.IsCorrectionCheckboxCheckedAsync());
        Assert.True(await vacancyDetail.IsCorrectionReasonFieldVisibleAsync(),
            "Expected the Correction Reason field to appear once the checkbox is checked");
        Assert.False(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be re-enabled while requesting a correction");
    }

    [Fact]
    public async Task UncheckingCorrectionCheckbox_HidesReasonField_AndRelocksDropdown()
    {
        var (_, vacancyDetail) = await ArrangeVacancyWithApplicationAsync();

        await vacancyDetail.SetAuthorisedCorrectionCheckedAsync(true);
        Assert.True(await vacancyDetail.IsCorrectionReasonFieldVisibleAsync());
        Assert.False(await vacancyDetail.IsPositionProfileDisabledAsync());

        await vacancyDetail.SetAuthorisedCorrectionCheckedAsync(false);

        Assert.False(await vacancyDetail.IsCorrectionCheckboxCheckedAsync());
        Assert.False(await vacancyDetail.IsCorrectionReasonFieldVisibleAsync(),
            "Expected the Correction Reason field to be hidden again after unchecking the box");
        Assert.True(await vacancyDetail.IsPositionProfileDisabledAsync(),
            "Expected the Position Profile dropdown to be re-locked after unchecking the box");
    }

    [Fact]
    public async Task AuthorisedCorrection_WithReasonAndNewProfile_SavesSuccessfully()
    {
        var (vacancyTitle, vacancyDetail) = await ArrangeVacancyWithApplicationAsync();
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await vacancyDetail.SetAuthorisedCorrectionCheckedAsync(true);
        await vacancyDetail.SelectPositionProfileAsync("HR Advisor");
        await vacancyDetail.FillCorrectionReasonAsync("Wrong profile was selected at creation time — correcting to HR Advisor.");

        await vacancyDetail.SaveExistingVacancyAsync();

        Assert.EndsWith("/vacancies", _page.Url);

        // Re-open the vacancy and confirm the Position Profile change was actually persisted.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.Equal("HR Advisor", await vacancyDetail.GetLinkedPositionProfileTitleAsync());
    }

    [Fact]
    public async Task AuthorisedCorrection_WithEmptyReason_ShowsValidationError_AndDoesNotSave()
    {
        var (_, vacancyDetail) = await ArrangeVacancyWithApplicationAsync();

        await vacancyDetail.SetAuthorisedCorrectionCheckedAsync(true);
        await vacancyDetail.SelectPositionProfileAsync("HR Advisor");
        // Deliberately leave Correction Reason blank.

        await vacancyDetail.ClickSaveButtonAsync();

        await _page.WaitForSelectorAsync(".validation-message, .alert-danger", new() { Timeout = 10_000 });
        Assert.True(await vacancyDetail.HasErrorAsync(),
            "Expected a validation error when saving an authorised correction with an empty Correction Reason");

        // Stayed on the vacancy's own edit route rather than the bare list URL a successful save
        // would have redirected to (EditPageBase.OnSavedAsync navigates to ListUrl on success).
        Assert.False(_page.Url.TrimEnd('/').EndsWith("/vacancies"),
            "Expected the save to have failed client/server validation rather than navigating to the vacancy list");
    }

    /// <summary>
    /// Creates a fresh vacancy linked to "Senior Software Engineer" (seeded for Acme — see
    /// EmployeesModule.SeedEmployeesAsync) and adds a candidate application to it, which drives
    /// UpdateVacancyHandler.CanChangePositionProfile to false (applicationCount &gt; 0) without
    /// needing to walk the vacancy through Open status. Leaves the caller on the vacancy's detail
    /// page (Overview tab), logged in as Marcus Diallo (Recruiter).
    /// </summary>
    private async Task<(string VacancyTitle, VacancyDetailPage VacancyDetail)> ArrangeVacancyWithApplicationAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast = $"CorrCand{unique}";
        var candidateEmail = $"e2e.corrcand{unique}@example.com";
        var candidateName =$"{candidateFirst} {candidateLast}";
        var vacancyTitle = $"E2E Correction {unique}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
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

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        // Back to the Overview tab, where the Position Profile card / correction section live.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        return (vacancyTitle, vacancyDetail);
    }
}
