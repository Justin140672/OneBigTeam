using System.Globalization;
using System.Net;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal recruitment Ticket 8 — the complete internal recruitment journey, end to end through the
/// UI, in ONE test so the hand-offs between the per-ticket features are protected (each ticket's own
/// class covers its screen in isolation):
///
///   recruiter advertises an Open vacancy internally (Vacancy detail "Advertise this vacancy to
///   employees") → a plain employee finds it on Internal Vacancies and applies with a CV → Applied
///   state (also after reload) and a duplicate submission is refused → the candidate is linked to the
///   existing employee and the application references the uploaded CV → the recruiter sees the
///   Internal badge in the Applications list and on the Kanban board → Review CV shows the exact
///   submitted CV → uploading a replacement CV on Candidate details changes the candidate's CURRENT
///   CV but not the application's → schedule interview, record outcome, make offer (Hire stays
///   disabled for the internal row) → Appoint → the EXISTING employee's department, position profile,
///   location and manager change while employee number / start date / continuous service / identity
///   stay put, no second employee exists, and the application is Hired.
///
/// Isolation: everything is this test's own, created through the real HR.Api — a fresh position
/// profile in Sales / Home (the fresh applicant starts in Engineering / London Office, so department
/// AND location both demonstrably change), a GUID-titled Open vacancy that starts NOT advertised, a
/// fresh Active applicant employee with its own login, and a fresh Active employee as the new
/// manager. Only seeded REFERENCE data is read (Acme, its departments/locations, James as hiring
/// manager and interviewer, the recruitment stage names).
///
/// Who drives the recruiter side: the process-wide "appointer" user from
/// InternalAppointmentApi.EnsureAppointerAsync (Employee + HR Administrator + Recruiter). The appoint
/// endpoint needs recruitment:manage AND employee:manage, which no seeded persona holds together, and
/// using one account for every recruiter step avoids an extra real-login switch mid-journey. The
/// appointer is never mutated by any test.
///
/// Serialization (same order as every other internal-recruitment class, so no deadlock):
///   1. CrossUserVacancyTestBase.GateInstance for the whole test — stage assertions read Acme's shared
///      recruitment pipeline by name, which RecruitmentStageManagementTests mutates.
///   2. SupabaseAuthGate only around provisioning logins and each sign-in / /api/login.
/// </summary>
public sealed class InternalRecruitmentJourneyTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    // RecruitmentStageSeeder.BuildDefaultStages.
    private const string InitialStage = "Application Received";
    private const string OfferStage = "Offer";
    private const string HiredStage = "Hired";

    private const string InterviewerName = "James";

    public override async Task InitializeAsync()
    {
        await CrossUserVacancyTestBase.GateInstance.WaitAsync();
        try
        {
            await base.InitializeAsync();
        }
        catch
        {
            CrossUserVacancyTestBase.GateInstance.Release();
            throw;
        }
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            CrossUserVacancyTestBase.GateInstance.Release();
        }
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Signs the browser in as <paramref name="email"/> while holding the Supabase gate.</summary>
    private async Task SignInAsAsync(LoginPage login, string email, bool switchAccount)
    {
        await SupabaseAuthGate.Instance.WaitAsync();
        try
        {
            if (switchAccount)
            {
                await login.SwitchAccountAsync(email);
            }
            else
            {
                await login.GoToAsync();
                await login.LoginAsync(email);
            }
        }
        finally
        {
            SupabaseAuthGate.Instance.Release();
        }
    }

    [Fact]
    public async Task InternalRecruitment_FullJourney_AdvertiseApplyReviewInterviewOfferAppoint_UpdatesTheExistingEmployee()
    {
        // ── Arrange (API): own vacancy (Open, NOT yet advertised), own manager, own applicant ──────
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var vacancy = await InternalRecruitmentJourneyApi.CreateVacancyAsync(
            hrAdminApi, recruiterApi, advertisedInternally: false, InternalRecruitmentJourneyApi.VacancyState.Open);
        var vacancyInfo = await InternalAppointmentApi.GetVacancyAsync(recruiterApi, vacancy.Id);
        var newProfileTitle = vacancyInfo.PositionProfileTitle
            ?? throw new InvalidOperationException("The arranged vacancy has no position profile title.");
        var newDepartmentName = await InternalAppointmentApi.GetDepartmentNameAsync(
            hrAdminApi, InternalRecruitmentJourneyApi.SalesDepartmentId);
        var newLocationName = vacancyInfo.EffectiveLocation
            ?? throw new InvalidOperationException("The arranged vacancy has no effective location.");

        var newManager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "JourneyMgr", activate: true);

        InternalAppointmentApi.Appointer appointer;
        InternalVacancyApplyApi.FreshEmployee applicant;
        await SupabaseAuthGate.Instance.WaitAsync();
        try
        {
            appointer = await InternalAppointmentApi.EnsureAppointerAsync(hrAdminApi, _fixture.ApiBaseUrl);
            applicant = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        }
        finally
        {
            SupabaseAuthGate.Instance.Release();
        }

        var before = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Id);
        Assert.NotEqual(InternalRecruitmentJourneyApi.SalesDepartmentId, before.DepartmentId);
        Assert.NotEqual(InternalRecruitmentJourneyApi.HomeLocationId, before.LocationId);
        Assert.NotEqual(vacancyInfo.PositionProfileId, before.PositionProfileId);
        Assert.NotEqual(newManager.Id, before.ManagerId);
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(hrAdminApi, applicant.WorkEmail));

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        var advertiseCheckbox = _page.Locator("#isAdvertisedInternally");

        // ── 1. Recruiter advertises the Open vacancy internally ────────────────────────────────
        await SignInAsAsync(login, appointer.WorkEmail, switchAccount: false);

        await vacancyDetail.GoToAsync(AcmeId, vacancy.Id);
        await Assertions.Expect(advertiseCheckbox).Not.ToBeCheckedAsync(new() { Timeout = 15_000 });
        await vacancyDetail.SetAdvertiseInternallyAsync(true);
        await vacancyDetail.SaveExistingVacancyAsync();

        await vacancyDetail.GoToAsync(AcmeId, vacancy.Id);
        await Assertions.Expect(advertiseCheckbox).ToBeCheckedAsync(new() { Timeout = 15_000 });

        // ── 2. The plain employee sees the vacancy and applies with a required CV ──────────────
        await SignInAsAsync(login, applicant.WorkEmail, switchAccount: true);

        var internalVacancies = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        await internalVacancies.GoToAsync(AcmeId);
        Assert.DoesNotContain("/access-denied", _page.Url);
        await internalVacancies.SearchAsync(vacancy.Title);
        Assert.True(await internalVacancies.HasCardAsync(vacancy.Title),
            "Expected the employee to see the newly advertised vacancy on Internal Vacancies");

        await internalVacancies.OpenCardAsync(vacancy.Title);
        await internalVacancies.ClickApplyAsync();

        // CV is required: submitting without one keeps the employee on the form.
        await internalVacancies.SubmitApplicationAsync();
        await internalVacancies.WaitForCvErrorAsync("Please choose a CV file to upload.");

        var submittedCvFileName = $"journey-cv-{applicant.LastName}.pdf";
        await internalVacancies.SelectValidCvAsync(submittedCvFileName, CandidateCvApi.BuildTestPdf());
        await internalVacancies.SubmitApplicationAsync();

        Assert.True(await internalVacancies.IsSuccessVisibleAsync(), "Expected the 'Application submitted' confirmation.");
        Assert.True(await internalVacancies.IsAppliedStateVisibleAsync(), "Expected the Applied state after submitting.");
        Assert.True(await internalVacancies.IsAppliedButtonDisabledAsync(), "Expected a disabled Applied button after submitting.");
        Assert.Equal(0, await internalVacancies.ApplyButtonCountAsync());

        // Applied comes back from the server after a reload.
        await internalVacancies.GoToAsync(AcmeId);
        await internalVacancies.SearchAsync(vacancy.Title);
        Assert.True(await internalVacancies.HasAppliedBadgeAsync(vacancy.Title),
            "Expected the vacancy card to show Applied after a reload");

        // A duplicate submission (a second POST as the same employee) is refused server-side.
        await SupabaseAuthGate.Instance.WaitAsync();
        HttpClient employeeApi;
        try
        {
            employeeApi = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, applicant.WorkEmail);
        }
        finally
        {
            SupabaseAuthGate.Instance.Release();
        }
        using (employeeApi)
        {
            using var duplicate = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                employeeApi, AcmeId, vacancy.Id, $"journey-cv-dup-{applicant.LastName}.pdf");
            await InternalRecruitmentJourneyApi.AssertStatusAsync(duplicate, HttpStatusCode.Conflict,
                "A second application for the same vacancy must be refused");
            Assert.Equal("already_applied", await InternalRecruitmentJourneyApi.ReadRejectionCodeAsync(duplicate));
        }

        // ── 3. Candidate linked to the existing employee; application references the uploaded CV ──
        var listed = Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
        Assert.Equal(applicant.WorkEmail, listed.CandidateEmail, ignoreCase: true);
        var applicationId = listed.Id;
        var candidateId = listed.CandidateId;

        var candidate = await InternalRecruitmentJourneyApi.GetCandidateAsync(recruiterApi, candidateId);
        Assert.Equal(applicant.Id, candidate.EmployeeId);

        var applied = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, applicationId);
        Assert.True(applied.IsInternal, "The employee's application must be an internal application");
        Assert.Equal(InitialStage, applied.CurrentStageName);
        Assert.Equal(submittedCvFileName, applied.CvFileName);
        var submittedCvId = applied.CvDocumentId
            ?? throw new InvalidOperationException("The internal application has no submitted CV document.");
        Assert.Equal(submittedCvId, applied.CurrentCandidateCvDocumentId);

        // ── 4. Recruiter sees the Internal badge in the Applications list and on the Kanban board ──
        await SignInAsAsync(login, appointer.WorkEmail, switchAccount: true);

        await vacancyDetail.GoToAsync(AcmeId, vacancy.Id);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ExpectApplicationRowTotalAsync(1);
        await vacancyDetail.ExpectApplicationRowInternalAsync(applicationId, isInternal: true);
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, InitialStage);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancy.Id);
        await kanban.ExpectCardInternalBadgeAsync(applicationId, isInternal: true);
        var (_, kanbanColumn) = await kanban.GetColumnOfCardAsync(applicationId);
        Assert.Equal(InitialStage, kanbanColumn);

        // ── 5. Review CV (from the Applications list) shows the exact submitted CV ─────────────
        await vacancyDetail.GoToAsync(AcmeId, vacancy.Id);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickReviewCvForAsync(applicant.LastName);

        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);
        await review.WaitForLoadedAsync();
        Assert.Equal(applicationId, review.GetApplicationIdFromUrl());
        await review.ExpectCandidateInternalBadgeAsync(isInternal: true);
        await review.ExpectCvPanelTitleAsync(ReviewCvPage.SubmittedCvTitle);
        Assert.Equal(submittedCvFileName, await review.GetSubmittedFileNameAsync());
        Assert.False(await review.IsNoSubmittedCvBannerVisibleAsync(),
            "An internal application always carries its submitted CV — no 'No CV was captured' banner");

        // ── 6. Replacing the candidate's CURRENT CV does not change the application's CV ────────
        var candidatePage = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await candidatePage.GoToAsync(AcmeId, candidateId);
        var candidateCvs = new CandidateCvDocumentsSection(_page);
        await candidateCvs.WaitForLoadedAsync();

        var replacementCvFileName = $"journey-cv-v2-{applicant.LastName}.pdf";
        await candidateCvs.UploadCvAsync(replacementCvFileName, CandidateCvApi.BuildTestPdf());
        await candidateCvs.ExpectRowCountAsync(2);
        Assert.True(await candidateCvs.RowHasCurrentBadgeAsync(replacementCvFileName),
            "The replacement must become the candidate's current CV");
        Assert.False(await candidateCvs.RowHasCurrentBadgeAsync(submittedCvFileName),
            "The originally submitted CV must no longer be the current CV");
        await candidateCvs.ExpectRowReferencedTextAsync(submittedCvFileName, "Submitted with 1 application(s)");

        var afterReplace = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, applicationId);
        Assert.Equal(submittedCvId, afterReplace.CvDocumentId);
        Assert.Equal(submittedCvFileName, afterReplace.CvFileName);
        Assert.Equal(replacementCvFileName, afterReplace.CurrentCandidateCvFileName);
        Assert.NotEqual(submittedCvId, afterReplace.CurrentCandidateCvDocumentId);

        await review.GoToAsync(AcmeId, vacancy.Id, applicationId);
        await review.ExpectCvPanelTitleAsync(ReviewCvPage.SubmittedCvTitle);
        Assert.Equal(submittedCvFileName, await review.GetSubmittedFileNameAsync());
        Assert.True(await review.IsUseCurrentCvVisibleAsync(),
            "Expected 'Use candidate's current CV' once the current CV differs from the submitted CV");

        // ── 7. Progress through the stages: interview → outcome → offer ────────────────────────
        await vacancyDetail.GoToAsync(AcmeId, vacancy.Id);
        await vacancyDetail.OpenApplicationsTabAsync();

        var interviewAt = Today.AddDays(1).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " 10:00";
        await vacancyDetail.ClickScheduleInterviewForAsync(applicant.LastName);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync(InterviewerName);
        await vacancyDetail.FillScheduledAtAsync(interviewAt);
        await vacancyDetail.SubmitScheduleInterviewAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, InitialStage);

        await vacancyDetail.OpenInterviewsTabAsync();
        Assert.Equal("Pending", await vacancyDetail.GetInterviewOutcomeAsync(applicant.LastName));
        await vacancyDetail.ClickRecordOutcomeForAsync(applicant.LastName);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();
        Assert.Equal("Passed", await vacancyDetail.GetInterviewOutcomeAsync(applicant.LastName));

        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickOfferForAsync(applicant.LastName);
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, OfferStage);

        // An internal application is completed by Appoint, never by Hire.
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Hire");

        // ── 8. Appoint the existing employee ───────────────────────────────────────────────────
        await vacancyDetail.ClickAppointForAsync(applicant.LastName);
        var dialog = new InternalAppointmentDialog(_page);
        await dialog.WaitForOpenAsync();

        await dialog.ExpectExistingEmployeeNoticeAsync(applicant.FullName);
        await dialog.ExpectDerivedFieldsAsync(newProfileTitle, newDepartmentName, newLocationName);
        // The offer was made without a proposed start date, so the appointment defaults to today.
        await dialog.ExpectEffectiveDateAsync(Today);

        await dialog.SelectManagerAsync(newManager.LastName);
        await dialog.ExpectManagerAsync(newManager.FullName);
        await dialog.SubmitExpectingSuccessAsync();

        await dialog.ExpectAppliedSuccessBannerAsync();
        await dialog.ExpectEmployeeLinkAsync(AcmeId, applicant.Id);

        // ── 9. Application reaches Hired ──────────────────────────────────────────────────────
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);
        await vacancyDetail.ExpectAppointmentPendingHintAsync(applicationId, visible: false);

        var hired = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, applicationId);
        Assert.Equal(HiredStage, hired.CurrentStageName);
        Assert.Equal(submittedCvId, hired.CvDocumentId);

        // ── 10. The EXISTING employee was updated; no second employee was created ────────────────
        var after = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Id);
        Assert.Equal(InternalRecruitmentJourneyApi.SalesDepartmentId, after.DepartmentId);
        Assert.Equal(vacancyInfo.PositionProfileId, after.PositionProfileId);
        Assert.Equal(InternalRecruitmentJourneyApi.HomeLocationId, after.LocationId);
        Assert.Equal(newManager.Id, after.ManagerId);

        Assert.Equal(before.EmployeeNumber, after.EmployeeNumber);
        Assert.Equal(before.StartDate, after.StartDate);
        Assert.Equal(before.ContinuousServiceDate, after.ContinuousServiceDate);
        Assert.Equal(before.EmploymentTypeId, after.EmploymentTypeId);
        Assert.Equal(before.WorkEmail, after.WorkEmail, ignoreCase: true);
        Assert.Equal(before.FirstName, after.FirstName);
        Assert.Equal(before.LastName, after.LastName);

        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(hrAdminApi, applicant.WorkEmail));
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(hrAdminApi, applicant.LastName));

        var candidateAfter = await InternalRecruitmentJourneyApi.GetCandidateAsync(recruiterApi, candidateId);
        Assert.Equal(applicant.Id, candidateAfter.EmployeeId);

        // The success banner links to that same employee's profile, showing the new manager and role.
        await dialog.FollowEmployeeLinkAsync(applicant.Id);
        await Assertions.Expect(_page.Locator("h1").Filter(new() { HasText = applicant.FullName }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.Locator("p").Filter(new() { HasText = "Reports To:" }).First)
            .ToContainTextAsync(newManager.FullName, new() { Timeout = 15_000 });
    }
}
