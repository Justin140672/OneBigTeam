using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class CandidateOfferDetailsAndResponseTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    private const decimal SalaryMin = 50_000m;
    private const decimal SalaryMax = 70_000m;

    [Fact]
    public async Task MakeOffer_ThenRecordAccepted_ThenHire_WithOfferContextThroughout()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var candidateLast = $"OfferAccept{unique}";
        var vacancyTitle = $"E2E Offer Role {unique}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var (candidate, vacancy) = await ArrangePreOfferApplicationAsync(login, vacancyDetail, unique, candidateLast, vacancyTitle);

        Assert.False(await vacancyDetail.IsRecordOfferResponseToolbarItemEnabledAsync(candidate),
            "Expected 'Record Offer Response' to be disabled before any offer has been made");

        await vacancyDetail.OpenMakeOfferDialogAsync(candidate);

        var context = await vacancyDetail.GetOfferPositionProfileContextTextAsync();
        Assert.NotNull(context);
        Assert.Contains("Salary range", context);
        Assert.Contains("50,000", context);
        Assert.Contains("70,000", context);

        var prePopulated = await vacancyDetail.GetOfferedSalaryValueAsync();
        Assert.Contains("50,000", prePopulated);

        await vacancyDetail.SetOfferedSalaryAsync("65000");
        await vacancyDetail.SelectOfferSalaryFrequencyAsync("Annual");
        await vacancyDetail.FillOfferProposedStartDateAsync("01/03/2027");
        await vacancyDetail.FillOfferDateAsync("05/02/2027");
        await vacancyDetail.FillOfferNotesAsync($"E2E offer notes {unique}");
        await vacancyDetail.SubmitOfferAsync();

        var badge = await vacancyDetail.GetOfferResponseBadgeTextAsync(candidate);
        Assert.NotNull(badge);
        Assert.Contains("Awaiting response", badge);

        Assert.True(await vacancyDetail.IsRecordOfferResponseToolbarItemEnabledAsync(candidate),
            "Expected 'Record Offer Response' to be enabled while the offer is AwaitingResponse");

        await vacancyDetail.OpenRecordOfferResponseDialogAsync(candidate);
        await vacancyDetail.SelectOfferResponseStatusAsync("Accepted");
        await vacancyDetail.SubmitOfferResponseAsync();

        var acceptedBadge = await vacancyDetail.GetOfferResponseBadgeTextAsync(candidate);
        Assert.NotNull(acceptedBadge);
        Assert.Contains("Accepted", acceptedBadge);

        Assert.False(await vacancyDetail.IsRecordOfferResponseToolbarItemEnabledAsync(candidate),
            "Expected 'Record Offer Response' to be disabled once the offer response has been recorded");

        await vacancyDetail.ClickHireForAsync(candidate);
        await vacancyDetail.WaitForHireDialogAsync();

        Assert.Equal("01/03/2027", await vacancyDetail.GetHireStartDateValueAsync());

        var hireContext = await vacancyDetail.GetHireOfferAcceptedContextTextAsync();
        Assert.NotNull(hireContext);
        Assert.Contains("65,000", hireContext);

        Assert.False(await vacancyDetail.IsHireOfferBlockedWarningVisibleAsync(),
            "Did not expect the hire-offer-blocked warning for an accepted offer");

        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");
        await vacancyDetail.FillHireAddressAsync("1 Test Street", "London", "SW1A 1AA");
        await vacancyDetail.SubmitHireAsync();

        Assert.Equal("Hired", await vacancyDetail.GetApplicationStatusAsync(candidate));
    }

    [Fact]
    public async Task RecordOfferResponse_Declined_ShowsDeclinedBadge_AndBlocksHire()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var candidateLast = $"OfferDecline{unique}";
        var vacancyTitle = $"E2E Decline Role {unique}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var (candidate, _) = await ArrangePreOfferApplicationAsync(login, vacancyDetail, unique, candidateLast, vacancyTitle);

        await vacancyDetail.OpenMakeOfferDialogAsync(candidate);
        await vacancyDetail.FillOfferProposedStartDateAsync("01/04/2027");
        await vacancyDetail.SubmitOfferAsync();

        Assert.Contains("Awaiting response", await vacancyDetail.GetOfferResponseBadgeTextAsync(candidate) ?? "");

        await vacancyDetail.OpenRecordOfferResponseDialogAsync(candidate);
        await vacancyDetail.SelectOfferResponseStatusAsync("Declined");
        await vacancyDetail.SubmitOfferResponseAsync();

        var badge = await vacancyDetail.GetOfferResponseBadgeTextAsync(candidate);
        Assert.NotNull(badge);
        Assert.Contains("Declined", badge);

        Assert.False(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidate, "Hire"),
            "Expected Hire to be disabled after the candidate declined the offer");
    }

    /// <summary>
    /// Logs in as Laura to create a fresh, uniquely-titled Position Profile with a salary range
    /// (Engineering / London Office / Standard leave policy — mandatory fields), switches to Marcus
    /// ONCE to create the candidate and publish a vacancy against it, then adds
    /// <paramref name="candidateLast"/> as an application. Schedules an interview and records it as Passed, because "Offer"
    /// requires every active interview stage to be passed. Returns (candidateLast,
    /// vacancyTitle).
    ///
    /// Deliberately does the Laura (HR Administrator) work FIRST and switches to Marcus (Recruiter)
    /// exactly once, rather than Marcus → Laura → Marcus: each <see cref="LoginPage.SwitchAccountAsync"/>
    /// call re-enters the same real-Supabase-login/persona-cache machinery
    /// <see cref="LoginPage.LoginAsync"/> uses (see PersonaLoginCache), which under 15-thread E2E
    /// concurrency can itself cost a full "invalidate + up to 5 attempts x 45s real login" cascade on
    /// a cache miss. A needless switch back to the SAME persona (Marcus → Laura → Marcus) triples this
    /// test's exposure to that worst case for no product-behavior reason — Candidate creation has no
    /// dependency on the Position Profile, so it can simply happen after switching to Marcus, in the
    /// same single Marcus session that also creates/publishes the Vacancy and adds the application.
    /// </summary>
    private async Task<(string Candidate, string Vacancy)> ArrangePreOfferApplicationAsync(
        LoginPage login,
        VacancyDetailPage vacancyDetail,
        string unique,
        string candidateLast,
        string vacancyTitle)
    {
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        var profileTitle = $"E2E Offer Profile {unique}";

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.FillSalaryRangeAsync(SalaryMin, SalaryMax);
        await ppEdit.SelectSalaryTypeAsync("Annual");
        await ppEdit.SaveAsync();

        await login.SwitchAccountAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync($"e2e.{candidateLast.ToLowerInvariant()}@example.com");
        await candidateEdit.SaveNewCandidateAsync();

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

        Assert.Equal("Application Received", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2099 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();

        await vacancyDetail.OpenInterviewsTabAsync();
        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();

        await vacancyDetail.OpenApplicationsTabAsync();

        return (candidateLast, vacancyTitle);
    }
}
