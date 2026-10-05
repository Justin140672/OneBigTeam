using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ApplicationToEmployeeFlowTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";


    [Fact]
    public async Task Candidate_Applies_Interviews_IsOffered_AndHired_BecomesEmployee()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Cand{unique}";
        var candidateEmail = $"e2e.cand{unique}@example.com";
        var vacancyTitle   = $"E2E Test Role {unique}";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList    = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail  = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        var employeeList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // A fresh Position Profile is required here rather than the seeded "Senior Software
        // Engineer" — that profile already has a permanently-open vacancy in seed data (see
        // PositionProfileTestHelpers' remarks), which the "one live vacancy per position profile"
        // rule would otherwise reject a second vacancy against. Still logged in as Laura
        // (HR Administrator, required for Position Profile creation) at this point.
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        Assert.True(await candidateList.HasCandidateAsync(candidateLast),
            $"Expected the new candidate '{candidateLast}' to appear in the candidate list");

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the new vacancy '{vacancyTitle}' to appear in the vacancy list");

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateLast);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal("Application Received", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2026 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();

        Assert.Equal("Interview", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.OpenInterviewsTabAsync();
        Assert.Equal("Pending", await vacancyDetail.GetInterviewOutcomeAsync(candidateLast));

        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();

        Assert.Equal("Passed", await vacancyDetail.GetInterviewOutcomeAsync(candidateLast));

        await vacancyDetail.OpenApplicationsTabAsync();
        Assert.Equal("Interview", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.ClickOfferForAsync(candidateLast);
        await AcceptOfferAsync(vacancyDetail, candidateLast);
        Assert.Equal("Offer", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();
        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");

        Assert.Equal(profileTitle, await vacancyDetail.GetHireDerivedPositionProfileTextAsync());
        Assert.Equal("London Office", await vacancyDetail.GetHireDerivedLocationTextAsync());
        Assert.Equal("Permanent", await vacancyDetail.GetHireDerivedEmploymentTypeTextAsync());

        await vacancyDetail.FillHireEmployeeNumberAsync($"E2E-{unique}");
        await vacancyDetail.FillHireAddressAsync("1 Test Street", "London", "SW1A 1AA");

        await vacancyDetail.SubmitHireAsync();

        Assert.Equal("Hired", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickCandidateAsync(candidateLast);
        Assert.True(await candidateEdit.HasHiredBannerAsync(),
            "Expected the candidate detail page to show the 'hired and linked to employee' banner");

        await login.SwitchAccountAsync(LauraEmail);
        await employeeList.GoToAsync(AcmeId);
        Assert.True(await employeeList.HasEmployeeAsync(candidateLast),
            $"Expected an employee named '{candidateLast}' to appear in the employee list after hiring");
    }

    [Fact]
    public async Task HireCandidateDialog_MissingNewlyRequiredFields_ShowsValidationError_AndDoesNotHire()
    {
        var (candidateLast, vacancyDetail, profileTitle) = await ArrangeOfferedApplicationAsync();

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();

        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Department"),
            "Expected the manual Department dropdown to no longer exist in the Hire dialog");
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Location"),
            "Expected the manual Location dropdown to no longer exist in the Hire dialog");
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Position Profile"),
            "Expected the manual Position Profile dropdown to no longer exist in the Hire dialog");
        Assert.Equal(profileTitle, await vacancyDetail.GetHireDerivedPositionProfileTextAsync());
        Assert.Equal("London Office", await vacancyDetail.GetHireDerivedLocationTextAsync());

        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");

        // ...but deliberately leave the newly-required manual fields (Employee Number, Employment
        // Type) blank and attempt to submit anyway. Department/Location/Position Profile are no
        // longer manual inputs at all as of the "Vacancy - Position Profile relationship" epic —
        // they're derived server-side from the Vacancy's linked Position Profile, so they can't be
        // "left blank" here the way they used to be.
        await vacancyDetail.ClickHireSubmitButtonAsync();

        await _page.WaitForSelectorAsync(".hire-candidate-dialog .alert-danger", new() { Timeout = 10_000 });
        Assert.True(await vacancyDetail.HasDialogErrorAsync("hire-candidate-dialog"),
            "Expected a validation error when submitting the Hire dialog without the newly required fields");

        await vacancyDetail.CancelHireDialogAsync();
        Assert.Equal("Offer", await vacancyDetail.GetApplicationStatusAsync(candidateLast));
    }

    [Fact]
    public async Task HireCandidateDialog_ShowsVacancyEmploymentType_PreselectsHiringManager_AndLeavesNationalityBlank()
    {
        var (candidateLast, vacancyDetail, _) = await ArrangeOfferedApplicationAsync();

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();

        Assert.Equal("Permanent", await vacancyDetail.GetHireDerivedEmploymentTypeTextAsync());
        Assert.False(await vacancyDetail.IsHireEmploymentTypeRequiredVisibleAsync());
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Employment Type"),
            "Expected no employment type dropdown in the Hire dialog; the type comes from the vacancy");

        Assert.Contains("James", await vacancyDetail.GetSelectedHireManagerTextAsync());
        Assert.Contains("hiring manager", await vacancyDetail.GetHireManagerHelpTextAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.True(string.IsNullOrEmpty(await vacancyDetail.GetSelectedHireNationalityTextAsync()),
            "Nationality must never be preselected or inferred in the Hire dialog");

        await vacancyDetail.CancelHireDialogAsync();
    }

    [Fact]
    public async Task HireCandidateDialog_AllowsNoManagerOverride_AndHires()
    {
        var (candidateLast, vacancyDetail, _) = await ArrangeOfferedApplicationAsync();

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();
        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");
        await vacancyDetail.FillHireEmployeeNumberAsync($"E2E-{Guid.NewGuid().ToString("N")[..8]}");
        await vacancyDetail.SelectHireManagerAsync("No Manager");
        await vacancyDetail.FillHireAddressAsync("1 Test Street", "London", "SW1A 1AA");

        await vacancyDetail.SubmitHireAsync();

        Assert.Equal("Hired", await vacancyDetail.GetApplicationStatusAsync(candidateLast));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HireCandidateDialog_BlankOrWhitespaceAddress_ShowsFieldErrors_AndDoesNotHire(string blank)
    {
        var (candidateLast, vacancyDetail, _) = await ArrangeOfferedApplicationAsync();

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();
        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");
        await vacancyDetail.FillHireEmployeeNumberAsync($"E2E-{Guid.NewGuid().ToString("N")[..8]}");
        await vacancyDetail.FillHireAddressAsync(blank, blank, blank);

        foreach (var fieldId in new[] { "hire-address-line-1", "hire-city", "hire-post-code" })
            Assert.Equal("true", await vacancyDetail.GetHireFieldAriaRequiredAsync(fieldId));

        await vacancyDetail.ClickHireSubmitButtonAsync();

        await _page.WaitForSelectorAsync(".hire-candidate-dialog .alert-danger", new() { Timeout = 10_000 });
        Assert.Contains("address line 1", await vacancyDetail.GetHireFieldErrorAsync("hire-address-line-1"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("city", await vacancyDetail.GetHireFieldErrorAsync("hire-city"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post code", await vacancyDetail.GetHireFieldErrorAsync("hire-post-code"), StringComparison.OrdinalIgnoreCase);

        await vacancyDetail.CancelHireDialogAsync();
        Assert.Equal("Offer", await vacancyDetail.GetApplicationStatusAsync(candidateLast));
    }

    private static async Task AcceptOfferAsync(VacancyDetailPage vacancyDetail, string candidateLast)
    {
        await vacancyDetail.OpenRecordOfferResponseDialogAsync(candidateLast);
        await vacancyDetail.SelectOfferResponseStatusAsync("Accepted");
        await vacancyDetail.SubmitOfferResponseAsync();
    }

    private async Task<(string CandidateLast, VacancyDetailPage VacancyDetail, string ProfileTitle)> ArrangeOfferedApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Cand{unique}";
        var candidateEmail = $"e2e.cand{unique}@example.com";
        var vacancyTitle   = $"E2E Test Role {unique}";

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

        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2026 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();

        await vacancyDetail.OpenInterviewsTabAsync();
        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();

        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickOfferForAsync(candidateLast);
        await AcceptOfferAsync(vacancyDetail, candidateLast);

        return (candidateLast, vacancyDetail, profileTitle);
    }
}
