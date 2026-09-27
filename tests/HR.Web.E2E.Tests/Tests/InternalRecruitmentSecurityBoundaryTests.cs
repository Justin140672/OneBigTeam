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
///
/// Serialization: same two gates, same order, as every other internal-recruitment class —
/// CrossUserVacancyTestBase.GateInstance for the whole test (applications land on Acme's shared
/// recruitment pipeline), SupabaseAuthGate only around provisioning logins and /api/login.
/// </summary>
public sealed class InternalRecruitmentSecurityBoundaryTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;
    private static readonly Guid BetaCorpId = InternalRecruitmentJourneyApi.BetaCorpId;

    private const string InitialStage = "Application Received";
    private const string HiredStage = "Hired";

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

    /// <summary>A fresh Active employee with a login, plus an HttpClient signed in AS that employee.</summary>
    private sealed record SignedInEmployee(InternalVacancyApplyApi.FreshEmployee Employee, HttpClient Api) : IDisposable
    {
        public void Dispose() => Api.Dispose();
    }

    private async Task<SignedInEmployee> CreateSignedInEmployeeAsync(HttpClient hrAdminApi)
    {
        await SupabaseAuthGate.Instance.WaitAsync();
        try
        {
            var employee = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
            var api = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, employee.WorkEmail);
            return new SignedInEmployee(employee, api);
        }
        finally
        {
            SupabaseAuthGate.Instance.Release();
        }
    }

    // ── 1. Impersonation ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_WithAnotherEmployeesIdentityInTheForm_IsFiledForTheSignedInEmployeeOnly()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var victim = await CreateSignedInEmployeeAsync(hrAdminApi);
        using var attacker = await CreateSignedInEmployeeAsync(hrAdminApi);

        // The attacker submits the apply form carrying every identity field a client might try.
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

        // Exactly one application, and it belongs to the ATTACKER — not the employee they named.
        var afterSpoof = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        var attackerApplication = Assert.Single(afterSpoof);
        Assert.Equal(attacker.Employee.WorkEmail, attackerApplication.CandidateEmail, ignoreCase: true);
        Assert.Equal(attacker.Employee.LastName, attackerApplication.CandidateLastName);

        var attackerCandidate = await InternalRecruitmentJourneyApi.GetCandidateAsync(recruiterApi, attackerApplication.CandidateId);
        Assert.Equal(attacker.Employee.Id, attackerCandidate.EmployeeId);

        var attackerDetail = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, attackerApplication.Id);
        Assert.True(attackerDetail.IsInternal);

        // Repeating the spoof is "already applied" for the attacker — it never becomes the victim's.
        using (var repeat = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   attacker.Api, AcmeId, vacancy.Id, $"cv2-{attacker.Employee.LastName}.pdf", spoofedIdentity))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(repeat, HttpStatusCode.Conflict,
                "A repeat submission by the attacker must be their own duplicate");
            Assert.Equal("already_applied", await InternalRecruitmentJourneyApi.ReadRejectionCodeAsync(repeat));
        }

        // The victim was never applied on their behalf: their own application is accepted (not a
        // duplicate) and is linked to their own employee record.
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

    // ── 2. Only Open + internally advertised vacancies accept internal applications ──────────

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

    // ── 3. Cross-company vacancy / company route on the employee apply endpoint ──────────────

    [Fact]
    public async Task Apply_WithAnotherCompanysVacancyOrCompanyRoute_IsRejected_AndCreatesNoApplication()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        using var applicant = await CreateSignedInEmployeeAsync(hrAdminApi);

        // Another company's vacancy id through the employee's own company route → simply not found.
        using (var foreignVacancy = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, AcmeId, InternalRecruitmentJourneyApi.BetaBackendVacancyId, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignVacancy, HttpStatusCode.NotFound,
                "Another company's vacancy id must not be reachable through the employee's own company");
        }

        // Another company in the route (with the employee's own, valid vacancy id) → refused outright.
        using (var foreignRoute = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, BetaCorpId, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignRoute, HttpStatusCode.Forbidden,
                "An employee must not be able to address another company's internal vacancies");
        }

        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));

        // The legitimate request still works — the rejections left nothing half-created.
        using (var legitimate = await InternalRecruitmentJourneyApi.PostInternalApplicationAsync(
                   applicant.Api, AcmeId, vacancy.Id, $"cv-{applicant.Employee.LastName}.pdf"))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(legitimate, HttpStatusCode.Created,
                "The employee's own application through their own company should succeed");
        }
        Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
    }

    // ── 4. Cross-company candidate / vacancy / application ids on the recruiter endpoints ─────

    [Fact]
    public async Task RecruiterEndpoints_WithAnotherCompanysVacancyCandidateOrApplication_AreRejected()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var unique = Guid.NewGuid().ToString("N")[..8];

        // new-candidate: another company's vacancy via our route → 404; another company's route → 403.
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

        // Add existing candidate: another company's candidate onto our own vacancy → 404.
        using (var foreignCandidate = await InternalRecruitmentJourneyApi.PostExistingCandidateApplicationAsync(
                   recruiterApi, AcmeId, vacancy.Id, InternalRecruitmentJourneyApi.BetaSophieCandidateId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignCandidate, HttpStatusCode.NotFound,
                "Another company's candidate must not be addable to this company's vacancy");
        }

        // Set application CV: another company's application (under its own or our vacancy) → 404.
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

        // Nothing was created on our vacancy by any of the above.
        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id));
    }

    // ── 5. An internal application's CV can't be swapped for another candidate's document ────

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

        // Someone else's CV (a different, external candidate in the same company).
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

    // ── 6. Cross-company ids on the appoint endpoint ─────────────────────────────────────────

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

        // Appointing needs recruitment:manage only, so the seeded Recruiter (Marcus — no
        // employee:manage) is the appointer; the employee record is read back as the HR Administrator.
        var appointerApi = recruiterApi;

        var before = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.NotEqual(vacancyInfo.PositionProfileId, before.PositionProfileId);
        Assert.NotNull(before.ManagerId);

        // Another company's employee as the new manager → not found; nothing changes.
        using (var foreignManager = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, AcmeId, vacancy.Id, application.ApplicationId, InternalRecruitmentJourneyApi.BetaAliceEmployeeId))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignManager, HttpStatusCode.NotFound,
                "Another company's employee must not be accepted as the new manager");
        }

        // Another company's application (under its own vacancy, or ours) → not found.
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

        // Another company in the route (with our own valid ids) → refused outright.
        using (var foreignRoute = await InternalRecruitmentJourneyApi.PostAppointAsync(
                   appointerApi, BetaCorpId, vacancy.Id, application.ApplicationId, managerId: null))
        {
            await InternalRecruitmentJourneyApi.AssertStatusAsync(foreignRoute, HttpStatusCode.Forbidden,
                "An appointer must not be able to address another company's applications");
        }

        // Nothing changed: same role and manager, application still on its initial stage.
        var unchanged = await InternalRecruitmentJourneyApi.GetEmployeeRecordAsync(hrAdminApi, applicant.Employee.Id);
        Assert.Equal(before.PositionProfileId, unchanged.PositionProfileId);
        Assert.Equal(before.DepartmentId, unchanged.DepartmentId);
        Assert.Equal(before.LocationId, unchanged.LocationId);
        Assert.Equal(before.ManagerId, unchanged.ManagerId);
        var stillOpen = await InternalRecruitmentJourneyApi.GetApplicationAsync(recruiterApi, vacancy.Id, application.ApplicationId);
        Assert.Equal(InitialStage, stillOpen.CurrentStageName);

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

    // ── 7. Appointing needs recruitment:manage — employee:manage alone is not enough ─────────

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

        // Laura (HR Administrator: employee:manage, no recruitment:manage) → 403, nothing changes.
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
        Assert.Equal(InitialStage, stillOpen.CurrentStageName);

        // Marcus (Recruiter: recruitment:manage, no employee:manage) → appoints successfully.
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
}
