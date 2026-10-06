using System.Net;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal recruitment Ticket 8 — security boundaries of the internal recruitment journey, probed
/// against the REAL deployed HR.Api (no browser steps; the per-screen UI behaviour is covered by
/// InternalVacancyApplyTests / InternalAppointmentTests / InternalRecruitmentJourneyTests):
///   • impersonation — the employee apply endpoint takes no identity from the request, so identity
///     fields for ANOTHER employee smuggled into the form are ignored;
///   • closed, draft and not-advertised vacancies cannot receive internal applications;
///   • cross-company vacancy, candidate, application and employee identifiers are rejected by the
///     apply, new-candidate / add-candidate, set-CV and appoint endpoints (and a foreign company in the
///     route is refused outright by TenantRouteAuthorizationMiddleware);
///   • another candidate's CV document can't be recorded against an internal application;
///   • appointing requires recruitment:manage only: the seeded Recruiter (Marcus, no employee:manage)
///     appoints, while an HR Administrator without recruitment:manage (Laura) is refused.
///
/// Cross-company identifiers are Beta Corp's SEEDED ids (a second tenant that exists in every E2E
/// environment — see InternalRecruitmentJourneyApi): its company, Alice (employee), the Backend
/// Engineer vacancy, and Sophie's candidate + application. They are only ever sent as FOREIGN ids by
/// Acme users, and every probe asserts rejection, so no Beta Corp data can be touched. Beta Corp has no
/// candidate DOCUMENTS and no E2E recruiter/HR persona, so "another company's CV document" is covered
/// by SetApplicationCvEndpointTests.Put_Returns_BadRequest_For_Document_Of_Another_Company instead.
///
/// Isolation: every test creates its own vacancy (fresh position profile, GUID title) and its own
/// Active employee(s) with fresh logins. After each rejected probe the test re-reads the affected data
/// to prove nothing was created or changed, and where it matters follows up with the legitimate call
/// to prove the rejection left nothing half-done.
/// </summary>
public sealed class InternalRecruitmentSecurityBoundaryTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;
    private static readonly Guid BetaCorpId = InternalRecruitmentJourneyApi.BetaCorpId;

    private const string InitialStage = "Application Received";
    private const string OfferStage = "Offer";
    private const string HiredStage = "Hired";

    private sealed record SignedInEmployee(InternalVacancyApplyApi.FreshEmployee Employee, HttpClient Api) : IDisposable
    {
        public void Dispose() => Api.Dispose();
    }

    private async Task<SignedInEmployee> CreateSignedInEmployeeAsync(HttpClient hrAdminApi)
    {
        var employee = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        var api = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, employee.WorkEmail);
        return new SignedInEmployee(employee, api);
    }


    [Fact]
    public async Task Apply_WithAnotherEmployeesIdentityInTheForm_IsFiledForTheSignedInEmployeeOnly()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var victim = await CreateSignedInEmployeeAsync(hrAdminApi);
        using var attacker = await CreateSignedInEmployeeAsync(hrAdminApi);

        var spoofedIdentity = new Dictionary<string, string>
        {
            ["EmployeeId"] = victim.Employee.Id.ToString(),
            ["ApplicantEmployeeId"] = victim.Employee.Id.ToString(),
            ["UserId"] = victim.Employee.Id.ToString(),
            ["FirstName"] = victim.Employee.FirstName,
            ["LastName"] = victim.Employee.LastName,
            ["Email"] = victim.Employee.WorkEmail,
            ["WorkEmail"] = victim.Employee.WorkEmail,
            ["Source"] = "Direct",
        };
        using (var spoofed = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   attacker.Api, AcmeId, vacancy.Id, $"cv-{attacker.Employee.LastName}.pdf", spoofedIdentity))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(spoofed, HttpStatusCode.Created,
                "The attacker's own application should be accepted (the spoofed fields are simply ignored)");
        }

        var afterSpoof = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        var attackerApplication = Assert.Single(afterSpoof);
        Assert.Equal(attacker.Employee.WorkEmail, attackerApplication.CandidateEmail, ignoreCase: true);
        Assert.Equal(attacker.Employee.LastName, attackerApplication.CandidateLastName);

        var attackerCandidate = await InternalRecruitmentJourneyApi.GetCandidateAsync(recruiterApi, attackerApplication.CandidateId);
        Assert.Equal(attacker.Employee.Id, attackerCandidate.EmployeeId);

        var attackerDetail = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, attackerApplication.Id);
        Assert.True(attackerDetail.IsInternal);

        using (var repeat = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   attacker.Api, AcmeId, vacancy.Id, $"cv2-{attacker.Employee.LastName}.pdf", spoofedIdentity))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(repeat, HttpStatusCode.Conflict,
                "A repeat submission by the attacker must be their own duplicate");
            Assert.Equal("already_applied", await InternalRecruitmentJourneyApi.ReadRejectionCodeAsync(repeat));
        }

        using (var victimApply = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   victim.Api, AcmeId, vacancy.Id, $"cv-{victim.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(victimApply, HttpStatusCode.Created,
                "The named employee must not already have an application created by someone else");
        }

        var afterVictim = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Equal(2, afterVictim.Count);
        var victimApplication = Assert.Single(afterVictim,
            a => string.Equals(a.CandidateEmail, victim.Employee.WorkEmail, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(attackerApplication.CandidateId, victimApplication.CandidateId);
        var victimCandidate = await InternalRecruitmentJourneyApi.GetCandidateAsync(recruiterApi, victimApplication.CandidateId);
        Assert.Equal(victim.Employee.Id, victimCandidate.EmployeeId);
    }


    [Fact]
    public async Task Apply_ToDraftClosedOrNotAdvertisedVacancy_IsNotFound_AndCreatesNoApplication()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var draft = await InternalRecruitmentJourneyApi.CreateVacancyAsync(
            hrAdminApi, recruiterApi, advertisedInternally: true, InternalRecruitmentJourneyApi.VacancyState.Draft);
        var closed = await InternalRecruitmentJourneyApi.CreateVacancyAsync(
            hrAdminApi, recruiterApi, advertisedInternally: true, InternalRecruitmentJourneyApi.VacancyState.Closed);
        var notAdvertised = await InternalRecruitmentJourneyApi.CreateVacancyAsync(
            hrAdminApi, recruiterApi, advertisedInternally: false, InternalRecruitmentJourneyApi.VacancyState.Open);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);

        foreach (var (label, vacancy) in new[] { ("Draft", draft), ("Closed", closed), ("Open but not advertised internally", notAdvertised) })
        {
            using var response = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                applicant.Api, AcmeId, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf");
            await InternalRecruitmentJourneyApi.AssertStatusAsync(response, HttpStatusCode.NotFound,
                $"A {label} vacancy must not accept an internal application");

            Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
        }
    }


    [Fact]
    public async Task Apply_WithAnotherCompanysVacancyOrCompanyRoute_IsRejected_AndCreatesNoApplication()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);

        using (var foreignVacancy = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, AcmeId, InternalRecruitmentJourneyApi.BetaBackendVacancyId, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignVacancy, HttpStatusCode.NotFound,
                "Another company's vacancy id must not be reachable through the employee's own company");
        }

        using (var foreignRoute = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, BetaCorpId, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignRoute, HttpStatusCode.Forbidden,
                "An employee must not be able to address another company's internal vacancies");
        }

        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));

        using (var legitimate = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, AcmeId, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(legitimate, HttpStatusCode.Created,
                "The employee's own application through their own company should succeed");
        }
        Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
    }


    [Fact]
    public async Task RecruiterEndpoints_WithAnotherCompanysVacancyCandidateOrApplication_AreRejected()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var unique = Guid.NewGuid().ToString("N")[..8];

        using (var foreignVacancy = await InternalRecruitmentJourneyApi.PostNewCandidateApplicationAsync(
                   recruiterApi, AcmeId, InternalRecruitmentJourneyApi.BetaBackendVacancyId, $"XVac{unique}"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignVacancy, HttpStatusCode.NotFound,
                "new-candidate must not accept another company's vacancy id");
        }
        using (var foreignRoute = await InternalRecruitmentJourneyApi.PostNewCandidateApplicationAsync(
                   recruiterApi, BetaCorpId, InternalRecruitmentJourneyApi.BetaBackendVacancyId, $"XRoute{unique}"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignRoute, HttpStatusCode.Forbidden,
                "A recruiter must not be able to address another company's vacancies");
        }

        using (var foreignCandidate = await InternalRecruitmentJourneyApi.PostExistingCandidateApplicationAsync(
                   recruiterApi, AcmeId, vacancy.Id, InternalRecruitmentJourneyApi.BetaSophieCandidateId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignCandidate, HttpStatusCode.NotFound,
                "Another company's candidate must not be addable to this company's vacancy");
        }

        var anyCvDocumentId = Guid.NewGuid();
        using (var foreignApplication = await InternalRecruitmentJourneyApi.PutApplicationCvAsync(
                   recruiterApi, AcmeId, InternalRecruitmentJourneyApi.BetaBackendVacancyId,
                   InternalRecruitmentJourneyApi.BetaSophieApplicationId, anyCvDocumentId, expectedVersion: 1))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignApplication, HttpStatusCode.NotFound,
                "Another company's application must not be reachable through this company's route");
        }
        using (var foreignApplicationOnOurVacancy = await InternalRecruitmentJourneyApi.PutApplicationCvAsync(
                   recruiterApi, AcmeId, vacancy.Id,
                   InternalRecruitmentJourneyApi.BetaSophieApplicationId, anyCvDocumentId, expectedVersion: 1))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignApplicationOnOurVacancy, HttpStatusCode.NotFound,
                "Another company's application id must not resolve under this company's vacancy");
        }

        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
    }


    [Fact]
    public async Task SetCv_OnInternalApplication_WithAnotherCandidatesCvDocument_IsRejected_AndSubmittedCvIsKept()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);
        var submittedCvFileName = $"cv-{applicant.Employee.LastName}.pdf";
        var internalApplication = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(applicant.Api, vacancy.Id, submittedCvFileName);

        var before = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, internalApplication.ApplicationId);
        Assert.Equal(submittedCvFileName, before.CvFileName);
        var submittedCvId = before.CvDocumentId
            ?? throw new InvalidOperationException("The internal application has no submitted CV document.");

        var unique = Guid.NewGuid().ToString("N")[..8];
        var otherCandidateId = await CandidateCvApi.CreateCandidateAsync(
            recruiterApi, AcmeId, "E2E", $"OtherCv{unique}", $"e2e.othercv.{unique}@example.com");
        var otherCvId = await CandidateCvApi.UploadCandidateCvAsync(recruiterApi, AcmeId, otherCandidateId, $"other-cv-{unique}.pdf");

        var version = (await CandidateCvApi.GetApplicationAsync(recruiterApi, AcmeId, vacancy.Id, internalApplication.ApplicationId)).Version;
        using (var swap = await InternalRecruitmentJourneyApi.PutApplicationCvAsync(
                   recruiterApi, AcmeId, vacancy.Id, internalApplication.ApplicationId, otherCvId, version))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(swap, HttpStatusCode.BadRequest,
                "An application must never reference another candidate's CV");
        }

        var after = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, internalApplication.ApplicationId);
        Assert.Equal(submittedCvId, after.CvDocumentId);
        Assert.Equal(submittedCvFileName, after.CvFileName);
    }


    [Fact]
    public async Task Appoint_WithAnotherCompanysManagerApplicationVacancyOrRoute_IsRejected_EmployeeUnchanged_ThenValidAppointSucceeds()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var vacancyInfo = await InternalAppointmentApi.GetVacancyAsync(recruiterApi, vacancy.Id);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);
        var application = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            applicant.Api, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf");

        var appointerApi = recruiterApi;

        var before = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.NotEqual(vacancyInfo.PositionProfileId, before.PositionProfileId);
        Assert.NotNull(before.ManagerId);

        await InternalOfferApi.ReachOfferStageAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        using (var foreignManagerOffer = await InternalOfferApi.PostOfferAsync(
                   recruiterApi, vacancy.Id, application.ApplicationId,
                   new InternalOfferApi.OfferInput(ManagerId: InternalRecruitmentJourneyApi.BetaAliceEmployeeId)))
        {
            await InternalOfferApi.AssertClientErrorAsync(foreignManagerOffer,
                "Another company's employee must not be accepted as the proposed manager of an offer");
        }

        await InternalOfferApi.MakeOfferAsync(
            recruiterApi, vacancy.Id, application.ApplicationId, new InternalOfferApi.OfferInput(NoManager: true));
        await InternalOfferApi.RespondAsEmployeeAsync(applicant.Api, application.ApplicationId, "Accept");

        using (var foreignManager = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, AcmeId, vacancy.Id, application.ApplicationId, InternalRecruitmentJourneyApi.BetaAliceEmployeeId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignManager, HttpStatusCode.BadRequest,
                "A manager other than the accepted one (here another company's employee) must not be accepted at appointment");
        }

        using (var foreignApplication = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, AcmeId, InternalRecruitmentJourneyApi.BetaBackendVacancyId,
                   InternalRecruitmentJourneyApi.BetaSophieApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignApplication, HttpStatusCode.NotFound,
                "Another company's vacancy/application must not be appointable through this company");
        }
        using (var foreignApplicationOnOurVacancy = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, AcmeId, vacancy.Id, InternalRecruitmentJourneyApi.BetaSophieApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignApplicationOnOurVacancy, HttpStatusCode.NotFound,
                "Another company's application id must not resolve under this company's vacancy");
        }

        using (var foreignRoute = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, BetaCorpId, vacancy.Id, application.ApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignRoute, HttpStatusCode.Forbidden,
                "An appointer must not be able to address another company's applications");
        }

        var unchanged = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(before.PositionProfileId, unchanged.PositionProfileId);
        Assert.Equal(before.DepartmentId, unchanged.DepartmentId);
        Assert.Equal(before.LocationId, unchanged.LocationId);
        Assert.Equal(before.ManagerId, unchanged.ManagerId);
        var stillOpen = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        Assert.Equal(OfferStage, stillOpen.CurrentStageName);

        // The rejected attempts released the application: a valid appointment now completes.
        using (var valid = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, AcmeId, vacancy.Id, application.ApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(valid, HttpStatusCode.OK,
                "A valid appointment should succeed after the rejected cross-company attempts");
        }

        var appointed = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(vacancyInfo.PositionProfileId, appointed.PositionProfileId);
        Assert.Null(appointed.ManagerId);
        Assert.Equal(before.EmployeeNumber, appointed.EmployeeNumber);
        Assert.Equal(before.StartDate, appointed.StartDate);
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(hrAdminApi, applicant.Employee.WorkEmail));

        var hired = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        Assert.Equal(HiredStage, hired.CurrentStageName);
    }


    [Fact]
    public async Task Appoint_ByHrAdministratorWithoutRecruitmentManage_IsForbidden_ThenRecruiterAppointSucceeds()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var vacancyInfo = await InternalAppointmentApi.GetVacancyAsync(recruiterApi, vacancy.Id);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);
        var application = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            applicant.Api, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf");

        var before = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);

        await InternalOfferApi.MakeAcceptedOfferAsync(
            recruiterApi, applicant.Api, vacancy.Id, application.ApplicationId, new InternalOfferApi.OfferInput(NoManager: true));

        using (var forbidden = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   hrAdminApi, AcmeId, vacancy.Id, application.ApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(forbidden, HttpStatusCode.Forbidden,
                "employee:manage without recruitment:manage must not be able to appoint");
        }

        var unchanged = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(before.PositionProfileId, unchanged.PositionProfileId);
        Assert.Equal(before.ManagerId, unchanged.ManagerId);
        var stillOpen = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        Assert.Equal(OfferStage, stillOpen.CurrentStageName);

        using (var appointed = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   recruiterApi, AcmeId, vacancy.Id, application.ApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(appointed, HttpStatusCode.OK,
                "recruitment:manage alone is sufficient to appoint");
        }

        var after = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(vacancyInfo.PositionProfileId, after.PositionProfileId);
        Assert.Null(after.ManagerId);
    }


    [Fact]
    public async Task InternalOffer_ByAnotherEmployeeStaleVersionOrWrongCompany_IsRejected_AndOfferStaysAwaitingResponse()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var recipient = await CreateSignedInEmployeeAsync(hrAdminApi);
        using var other = await CreateSignedInEmployeeAsync(hrAdminApi);
        var application = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            recipient.Api, vacancy.Id, $"cv-{recipient.Employee.LastName}.pdf");

        await InternalOfferApi.ReachOfferStageAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        await InternalOfferApi.MakeOfferAsync(
            recruiterApi, vacancy.Id, application.ApplicationId, new InternalOfferApi.OfferInput(NoManager: true));

        var offer = await InternalOfferApi.GetInternalOfferAsync(recipient.Api, application.ApplicationId);
        Assert.True(offer.IsOfferRecipient);
        Assert.True(offer.CanRespond);
        Assert.Equal("AwaitingResponse", offer.Terms.OfferResponseStatus);
        var version = offer.Terms.OfferVersion;

        using (var otherGet = await InternalOfferApi.GetInternalOfferRawAsync(other.Api, AcmeId, application.ApplicationId))
        {
            await InternalOfferApi.AssertStatusAsync(otherGet, HttpStatusCode.NotFound,
                "Another employee must not be able to read someone else's internal offer");
        }

        using (var otherRespond = await InternalOfferApi.PostEmployeeResponseRawAsync(
                   other.Api, AcmeId, application.ApplicationId, "Accept", version))
        {
            await InternalOfferApi.AssertStatusAsync(otherRespond, HttpStatusCode.NotFound,
                "Another employee must not be able to respond to someone else's internal offer");
        }

        using (var foreignRouteGet = await InternalOfferApi.GetInternalOfferRawAsync(recipient.Api, BetaCorpId, application.ApplicationId))
        {
            await InternalOfferApi.AssertStatusAsync(foreignRouteGet, HttpStatusCode.Forbidden,
                "An employee must not be able to address another company's internal offers");
        }

        using (var foreignRouteRespond = await InternalOfferApi.PostEmployeeResponseRawAsync(
                   recipient.Api, BetaCorpId, application.ApplicationId, "Accept", version))
        {
            await InternalOfferApi.AssertStatusAsync(foreignRouteRespond, HttpStatusCode.Forbidden,
                "An employee must not be able to respond through another company's route");
        }

        using (var stale = await InternalOfferApi.PostEmployeeResponseRawAsync(
                   recipient.Api, AcmeId, application.ApplicationId, "Accept", version + 1))
        {
            await InternalOfferApi.AssertStatusAsync(stale, HttpStatusCode.Conflict,
                "A response to a version other than the current offer must be refused");
        }

        var recruiterView = await InternalOfferApi.GetInternalOfferAsync(recruiterApi, application.ApplicationId);
        Assert.False(recruiterView.IsOfferRecipient);
        Assert.False(recruiterView.CanRespond);

        using (var recruiterRespond = await InternalOfferApi.PostEmployeeResponseRawAsync(
                   recruiterApi, AcmeId, application.ApplicationId, "Accept", version))
        {
            await InternalOfferApi.AssertClientErrorAsync(recruiterRespond,
                "A recruiter who is not the offer recipient must not be able to accept it through the employee endpoint");
        }

        var stillAwaiting = await InternalOfferApi.GetInternalOfferAsync(recipient.Api, application.ApplicationId);
        Assert.Equal("AwaitingResponse", stillAwaiting.Terms.OfferResponseStatus);
        Assert.True(stillAwaiting.CanRespond);

        await InternalOfferApi.RespondAsEmployeeAsync(recipient.Api, application.ApplicationId, "Accept");

        using (var flip = await InternalOfferApi.PostEmployeeResponseRawAsync(
                   recipient.Api, AcmeId, application.ApplicationId, "Decline", version, "Changed my mind"))
        {
            await InternalOfferApi.AssertStatusAsync(flip, HttpStatusCode.Conflict,
                "An accepted offer cannot be flipped to declined through the employee endpoint");
        }

        var accepted = await InternalOfferApi.GetInternalOfferAsync(recipient.Api, application.ApplicationId);
        Assert.Equal("Accepted", accepted.Terms.OfferResponseStatus);
        Assert.False(accepted.CanRespond);
    }


    [Fact]
    public async Task Appoint_WhileOfferIsAwaitingResponseOrDeclined_IsRejected_AndEmployeeUnchanged()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);
        var application = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            applicant.Api, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf");
        var before = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);

        using (var noOffer = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   recruiterApi, AcmeId, vacancy.Id, application.ApplicationId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(noOffer, HttpStatusCode.BadRequest,
                "Appointing without any internal offer must be refused");
        }

        await InternalOfferApi.ReachOfferStageAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        await InternalOfferApi.MakeOfferAsync(
            recruiterApi, vacancy.Id, application.ApplicationId, new InternalOfferApi.OfferInput(NoManager: true));

        using (var awaiting = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   recruiterApi, AcmeId, vacancy.Id, application.ApplicationId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(awaiting, HttpStatusCode.BadRequest,
                "Appointing while the offer is awaiting the employee's response must be refused");
        }

        await InternalOfferApi.RespondAsEmployeeAsync(applicant.Api, application.ApplicationId, "Decline", "Not for me");

        using (var declined = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   recruiterApi, AcmeId, vacancy.Id, application.ApplicationId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(declined, HttpStatusCode.BadRequest,
                "Appointing after the employee declined must be refused");
        }

        var after = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(before.PositionProfileId, after.PositionProfileId);
        Assert.Equal(before.DepartmentId, after.DepartmentId);
        Assert.Equal(before.ManagerId, after.ManagerId);
        var application2 = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        Assert.Equal(OfferStage, application2.CurrentStageName);
    }
}
