using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal vacancy offers: the recruiter's "Make an Offer" / "Revise Offer" dialog for an INTERNAL
/// application, the employee's task and secure review page (accept / decline), and how the recruiter's
/// Applications tab and Appoint dialog react.
///
/// Isolation: every test arranges its own GUID-titled Open internally-advertised vacancy, a brand-new
/// Active applicant employee with a login and a genuine internal application, and (where the behaviour
/// under test starts after the offer) makes the offer through the real HR.Api. Setup is by API; the
/// behaviour under test is driven through the UI. Only seeded reference data is read (Acme, James as the
/// vacancy's hiring manager, the recruitment stage names).
/// </summary>
public sealed class InternalOfferTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    private const string JamesFullName = "James Okafor";
    private const string OfferStage = "Offer";
    private const string HiredStage = "Hired";
    private const string TaskOpen = "open";
    private const string TaskCompleted = "completed";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private sealed record Arranged(
        HttpClient HrAdminApi,
        HttpClient RecruiterApi,
        HttpClient EmployeeApi,
        InternalVacancyApplyApi.FreshVacancy Vacancy,
        InternalAppointmentApi.VacancySnapshot VacancyDetail,
        InternalVacancyApplyApi.FreshEmployee Applicant,
        Guid ApplicationId) : IDisposable
    {
        public void Dispose()
        {
            HrAdminApi.Dispose();
            RecruiterApi.Dispose();
            EmployeeApi.Dispose();
        }
    }

    private async Task<Arranged> ArrangeAsync(InternalOfferApi.OfferInput? offer = null, bool accepted = false)
    {
        var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var vacancyDetail = await InternalAppointmentApi.GetVacancyAsync(recruiterApi, vacancy.Id);

        var applicant = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        var employeeApi = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, applicant.WorkEmail);
        var application = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            employeeApi, vacancy.Id, $"cv-{applicant.LastName}.pdf");

        var arranged = new Arranged(hrAdminApi, recruiterApi, employeeApi, vacancy, vacancyDetail, applicant, application.ApplicationId);

        if (offer is not null)
        {
            if (accepted)
            {
                await InternalOfferApi.MakeAcceptedOfferAsync(recruiterApi, employeeApi, vacancy.Id, application.ApplicationId, offer);
            }
            else
            {
                await InternalOfferApi.ReachOfferStageAsync(recruiterApi, vacancy.Id, application.ApplicationId);
                await InternalOfferApi.MakeOfferAsync(recruiterApi, vacancy.Id, application.ApplicationId, offer);
            }
        }

        return arranged;
    }

    private LoginPage Login => new(_page, _fixture.WebBaseUrl);

    private async Task SignInAsRecruiterAsync()
    {
        await Login.GoToAsync();
        await Login.LoginAsync(InternalAppointmentApi.RecruiterEmail);
    }

    private Task SwitchToAsync(string email) => Login.SwitchAccountAsync(email);

    private async Task<VacancyDetailPage> OpenApplicationsAsync(Arranged arranged)
    {
        var page = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await page.GoToAsync(AcmeId, arranged.Vacancy.Id);
        await page.OpenApplicationsTabAsync();
        await page.ExpectApplicationRowInternalAsync(arranged.ApplicationId, isInternal: true);
        return page;
    }

    private InternalOfferReviewPage ReviewPage => new(_page, _fixture.WebBaseUrl);


    [Fact]
    public async Task OfferDialog_ForInternalApplication_ShowsInternalFieldsAndDefaults_AndRequiresStartDate()
    {
        using var arranged = await ArrangeAsync();
        await InternalOfferApi.ReachOfferStageAsync(arranged.RecruiterApi, arranged.Vacancy.Id, arranged.ApplicationId);
        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        var dialog = await vacancyDetail.OpenInternalOfferDialogAsync(arranged.Applicant.LastName);

        await dialog.ExpectInternalNoticeAsync();
        await dialog.ExpectCurrencyAsync("GBP");
        await dialog.ExpectManagerAsync(JamesFullName);

        await dialog.SubmitExpectingValidationAsync("Please select a proposed start date.");

        await dialog.SelectManagerAsync("No manager");
        await dialog.ExpectManagerAsync("No manager");

        await dialog.CancelAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(arranged.Applicant.LastName, "Interview");    }


    [Fact]
    public async Task OfferDialog_ForExternalApplication_HidesTheInternalFields()
    {
        using var arranged = await ArrangeAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var externalLastName = $"External{unique}";
        var candidateId = await CandidateCvApi.CreateCandidateAsync(
            arranged.RecruiterApi, AcmeId, "E2E", externalLastName, $"e2e.ext.{unique}@example.com");
        var externalApplicationId = await CandidateCvApi.CreateApplicationAsync(
            arranged.RecruiterApi, AcmeId, arranged.Vacancy.Id, candidateId);
        await InternalOfferApi.ReachOfferStageAsync(arranged.RecruiterApi, arranged.Vacancy.Id, externalApplicationId);

        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        var dialog = await vacancyDetail.OpenInternalOfferDialogAsync(externalLastName);

        await dialog.ExpectInternalFieldsVisibleAsync(visible: false);
        await dialog.CancelAsync();
    }


    [Fact]
    public async Task Journey_RecruiterOffersInternally_EmployeeReviewsAndAccepts_RecruiterAppoints()
    {
        var newManager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "OfferMgr", activate: true);
        using var arranged = await ArrangeAsync();
        await InternalOfferApi.ReachOfferStageAsync(arranged.RecruiterApi, arranged.Vacancy.Id, arranged.ApplicationId);
        var applicant = arranged.Applicant;
        var deadline = Today.AddDays(7);
        var notes = $"E2E internal offer {applicant.LastName}";

        var departmentName = await InternalAppointmentApi.GetDepartmentNameAsync(
            arranged.HrAdminApi,
            arranged.VacancyDetail.PositionProfileDepartmentId
                ?? throw new InvalidOperationException("The arranged vacancy's position profile has no department."));
        var locationName = arranged.VacancyDetail.EffectiveLocation
            ?? throw new InvalidOperationException("The arranged vacancy has no effective location.");

        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        var offerDialog = await vacancyDetail.OpenInternalOfferDialogAsync(applicant.LastName);
        await offerDialog.ExpectInternalNoticeAsync();
        await offerDialog.ExpectManagerAsync(JamesFullName);
        await offerDialog.FillAsync(new InternalOfferTerms(
            Manager: newManager.LastName,
            StartDate: Today,
            Salary: "52000",
            Currency: "GBP",
            ResponseDeadline: deadline,
            HoursPerWeek: "37.5",
            Fte: "1",
            Notes: notes));
        await offerDialog.ExpectManagerAsync(newManager.FullName);
        await offerDialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, OfferStage);
        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Awaiting response");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Record Offer Response");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Appoint");

        await SwitchToAsync(applicant.WorkEmail);

        var review = ReviewPage;
        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 1);
        await review.ExpectOfferTaskTitleAsync("E2E");
        await review.OpenOfferTaskAsync();
        await review.ClickReviewOfferFromTaskAsync(arranged.ApplicationId);

        await review.ExpectNoticesAsync();
        await review.ExpectStatusAsync(1, "Awaiting your response");
        await review.ExpectTermAsync("job-title", "E2E Apply");
        await review.ExpectTermExactAsync("department", departmentName);
        await review.ExpectTermExactAsync("location", locationName);
        await review.ExpectTermPopulatedAsync("employment-type");
        await review.ExpectTermExactAsync("manager", newManager.FullName);
        await review.ExpectTermAsync("effective-date", InternalOfferReviewPage.DisplayDatePattern(Today));
        await review.ExpectTermAsync("salary", "GBP 52,000.00 (annual)");
        await review.ExpectTermVisibleAsync("working-days");
        await review.ExpectTermVisibleAsync("hours-per-day");
        await review.ExpectTermExactAsync("hours-per-week", "37.5");
        await review.ExpectTermExactAsync("fte", "1");
        await review.ExpectTermVisibleAsync("probation");
        await review.ExpectTermAsync("offer-date", InternalOfferReviewPage.DisplayDatePattern(Today));
        await review.ExpectTermAsync("response-deadline", InternalOfferReviewPage.DisplayDatePattern(deadline));
        await review.ExpectTermExactAsync("notes", notes);
        await review.ExpectRespondedAtVisibleAsync(visible: false);
        await review.ExpectActionsVisibleAsync();

        await review.AcceptAsync();
        await review.ExpectStatusAsync(1, "Accepted");
        await review.ExpectRespondedAtVisibleAsync(visible: true);
        await review.ExpectNoActionsAsync();
        await review.ExpectNoErrorAsync();

        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 0);
        await review.ExpectOfferTaskRowsAsync(TaskCompleted, 1);

        await SwitchToAsync(InternalAppointmentApi.RecruiterEmail);
        vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, OfferStage);
        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Accepted");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");

        await vacancyDetail.ClickAppointForAsync(applicant.LastName);
        var appointDialog = new InternalAppointmentDialog(_page);
        await appointDialog.WaitForOpenAsync();
        await appointDialog.ExpectAcceptedTermsAsync(Today, newManager.FullName, "GBP 52,000.00");
        await appointDialog.ExpectAcceptedTermsWithoutCompensationChangeAsync();
        await appointDialog.ExpectEffectiveDateAsync(Today);
        await appointDialog.ExpectManagerAsync(newManager.FullName);
        await appointDialog.SubmitExpectingSuccessAsync();

        await appointDialog.ExpectAppliedSuccessBannerAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);

        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);
        Assert.Equal(newManager.Id, after.ManagerId);
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(arranged.HrAdminApi, applicant.LastName));

        var compensation = await InternalAppointmentApi.GetCurrentCompensationAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(52000m, compensation.Salary);
        Assert.Equal("GBP", compensation.Currency);
    }


    [Fact]
    public async Task Employee_DeclinesWithReason_RecruiterSeesDeclined_AndAppointStaysDisabled()
    {
        using var arranged = await ArrangeAsync(
            new InternalOfferApi.OfferInput(ManagerId: InternalVacancyApplyApi.JamesId, StartDate: Today));
        var applicant = arranged.Applicant;

        await Login.GoToAsync();
        await Login.LoginAsync(applicant.WorkEmail);

        var review = ReviewPage;
        await review.GoToAsync(AcmeId, arranged.ApplicationId);
        await review.ExpectStatusAsync(1, "Awaiting your response");
        await review.ExpectTermExactAsync("manager", JamesFullName);
        await review.ExpectActionsVisibleAsync();

        await review.OpenDeclinePanelAsync();
        await review.CancelDeclineAsync();

        await review.DeclineAsync("Not the right time for a move");
        await review.ExpectStatusAsync(1, "Declined");
        await review.ExpectRespondedAtVisibleAsync(visible: true);
        await review.ExpectNoActionsAsync();
        await review.ExpectCannotRespondAsync("already been declined");

        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 0);
        await review.ExpectOfferTaskRowsAsync(TaskCompleted, 1);

        await SwitchToAsync(InternalAppointmentApi.RecruiterEmail);
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Declined");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Reject");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Appoint");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Revise Offer");

        var employee = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.NotEqual(arranged.VacancyDetail.PositionProfileId, employee.PositionProfileId);
    }


    [Fact]
    public async Task Appoint_IsDisabledWhileTheOfferAwaitsResponse_AndRecordOfferResponseIsTheFallback()
    {
        using var arranged = await ArrangeAsync(
            new InternalOfferApi.OfferInput(ManagerId: InternalVacancyApplyApi.JamesId, StartDate: Today));
        var applicant = arranged.Applicant;

        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, OfferStage);
        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Awaiting response");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Record Offer Response");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Revise Offer");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Appoint");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Hire");
    }


    [Fact]
    public async Task ReviseOffer_AfterAcceptance_RequiresFreshAcceptanceBeforeAppoint()
    {
        using var arranged = await ArrangeAsync(
            new InternalOfferApi.OfferInput(ManagerId: InternalVacancyApplyApi.JamesId, StartDate: Today, Salary: 42000m),
            accepted: true);
        var applicant = arranged.Applicant;

        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Accepted");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Revise Offer");

        var reviseDialog = await vacancyDetail.OpenReviseOfferDialogAsync(applicant.LastName);
        await reviseDialog.ExpectInternalNoticeAsync();
        await reviseDialog.ExpectSalaryContainsAsync("42,000");
        await reviseDialog.ExpectManagerAsync(JamesFullName);
        await reviseDialog.ExpectProposedStartDateAsync(Today);
        await reviseDialog.FillAsync(new InternalOfferTerms(Salary: "47000", Manager: "No manager", StartDate: Today));
        await reviseDialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Awaiting response");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Record Offer Response");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Appoint");

        var revised = await InternalOfferApi.GetInternalOfferAsync(arranged.EmployeeApi, arranged.ApplicationId);
        Assert.Equal(2, revised.Terms.OfferVersion);
        Assert.Equal("AwaitingResponse", revised.Terms.OfferResponseStatus);
        Assert.True(revised.CanRespond);

        await SwitchToAsync(applicant.WorkEmail);

        var review = ReviewPage;
        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskCompleted, 1);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 1);

        await review.GoToAsync(AcmeId, arranged.ApplicationId);
        await review.ExpectStatusAsync(2, "Awaiting your response");
        await review.ExpectTermAsync("salary", "GBP 47,000.00 (annual)");
        await review.ExpectTermExactAsync("manager", "No manager");
        await review.ExpectActionsVisibleAsync();

        await review.AcceptAsync();
        await review.ExpectStatusAsync(2, "Accepted");

        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 0);
        await review.ExpectOfferTaskRowsAsync(TaskCompleted, 2);

        await SwitchToAsync(InternalAppointmentApi.RecruiterEmail);
        vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Accepted");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");

        await vacancyDetail.ClickAppointForAsync(applicant.LastName);
        var appointDialog = new InternalAppointmentDialog(_page);
        await appointDialog.WaitForOpenAsync();
        await appointDialog.ExpectAcceptedTermsAsync(Today, "No manager", "GBP 47,000.00");
        await appointDialog.ExpectManagerAsync("No manager");
        await appointDialog.CancelAsync();
    }


    [Fact]
    public async Task RecruiterRecordsOfferResponse_AsFallback_ClosesTheEmployeesTask()
    {
        using var arranged = await ArrangeAsync(
            new InternalOfferApi.OfferInput(ManagerId: InternalVacancyApplyApi.JamesId, StartDate: Today));
        var applicant = arranged.Applicant;

        await SignInAsRecruiterAsync();
        var vacancyDetail = await OpenApplicationsAsync(arranged);

        await vacancyDetail.OpenRecordOfferResponseDialogAsync(applicant.LastName);
        await vacancyDetail.SelectOfferResponseStatusAsync("Accepted");
        await vacancyDetail.SubmitOfferResponseAsync();

        await vacancyDetail.ExpectActionSuccessMessageAsync("Offer response recorded");
        await vacancyDetail.ExpectOfferResponseBadgeAsync(applicant.LastName, "Offer: Accepted");
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");

        await SwitchToAsync(applicant.WorkEmail);

        var review = ReviewPage;
        await review.GoToTasksTabAsync(AcmeId, applicant.Id);
        await review.ExpectOfferTaskRowsAsync(TaskOpen, 0);
        await review.ExpectOfferTaskRowsAsync(TaskCompleted, 1);

        await review.GoToAsync(AcmeId, arranged.ApplicationId);
        await review.ExpectStatusAsync(1, "Accepted");
        await review.ExpectNoActionsAsync();
        await review.ExpectCannotRespondAsync("already been accepted");
    }


    [Fact]
    public async Task ReviewPage_ForAnotherEmployee_ShowsUnavailable_AndRecruiterSeesNoRespondActions()
    {
        using var arranged = await ArrangeAsync(
            new InternalOfferApi.OfferInput(ManagerId: InternalVacancyApplyApi.JamesId, StartDate: Today));
        var other = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(arranged.HrAdminApi, _fixture.ApiBaseUrl);

        await Login.GoToAsync();
        await Login.LoginAsync(other.WorkEmail);

        var review = ReviewPage;
        await review.GoToAsync(AcmeId, arranged.ApplicationId);
        await review.ExpectUnavailableWithNoOfferDetailsAsync();

        await review.GoToAsync(AcmeId, Guid.NewGuid());
        await review.ExpectUnavailableWithNoOfferDetailsAsync();

        await SwitchToAsync(InternalAppointmentApi.RecruiterEmail);
        await review.GoToAsync(AcmeId, arranged.ApplicationId);
        await review.ExpectStatusAsync(1, "Awaiting your response");
        await review.ExpectNoActionsAsync();

        var stillAwaiting = await InternalOfferApi.GetInternalOfferAsync(arranged.EmployeeApi, arranged.ApplicationId);
        Assert.Equal("AwaitingResponse", stillAwaiting.Terms.OfferResponseStatus);
    }
}
