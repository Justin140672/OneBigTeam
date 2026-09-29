using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal recruitment Ticket 6 — internal applications are identified throughout recruitment:
/// the shared "Internal" badge (InternalApplicationBadge.razor, data-testid="internal-application-badge")
/// on the vacancy Applications tab, the Kanban card, the Review CV page and the candidate detail
/// Applications card; the Applications tab's All/Internal/External filter; the candidate detail
/// "current employee (internal applicant)" alert; and the reports' Applications filter.
///
/// Isolation: every test arranges its OWN data through the real HR.Api — a brand-new, GUID-titled,
/// internally advertised Open vacancy (InternalVacancyApplyApi.CreateOpenInternalVacancyAsync), then
/// on that vacancy:
///   • an INTERNAL application: a brand-new Active employee with a freshly provisioned login applies
///     via POST .../internal-vacancies/{id}/applications as themselves (the same request the
///     employee Apply button sends), so Source == Internal;
///   • an EXTERNAL application: a brand-new candidate added by the recruiter (Marcus) via
///     POST .../vacancies/{id}/applications, the same endpoint the Add Candidate dialog uses.
/// Every assertion is scoped to those ids (data-application-id) or to the unique vacancy title, so
/// nothing depends on other tests' data. The recruiter UI session is Marcus Diallo (Recruiter), who
/// holds recruitment:manage, candidate:view and reporting:view-recruitment.
/// </summary>
public sealed class InternalApplicationIdentificationTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";

    // RecruitmentStageSeeder.BuildDefaultStages — both the internal apply and the recruiter's
    // CreateApplication start an application on the first active non-terminal stage.
    private const string InitialStage = "Application Received";

    private sealed record Arranged(
        Guid VacancyId,
        string VacancyTitle,
        Guid InternalApplicationId,
        Guid InternalCandidateId,
        string InternalLastName,
        Guid ExternalApplicationId,
        Guid ExternalCandidateId,
        string ExternalLastName);

    /// <summary>
    /// Creates this test's own vacancy with one internal and one external application (see class
    /// remarks), then signs the browser in as Marcus (Recruiter).
    /// </summary>
    private async Task<Arranged> ArrangeAsync()
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        using var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        InternalVacancyApplyApi.FreshEmployee employee;
        InternalVacancyApplyApi.InternalApplication internalApplication;
        employee = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        using var employeeApi = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, employee.WorkEmail);
        internalApplication = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
            employeeApi, vacancy.Id, $"cv-{employee.LastName}.pdf");

        var unique = Guid.NewGuid().ToString("N")[..8];
        var externalLastName = $"External{unique}";
        var externalCandidateId = await CandidateCvApi.CreateCandidateAsync(
            recruiterApi, AcmeId, "E2E", externalLastName, $"e2e.ext.{unique}@example.com");
        var externalApplicationId = await CandidateCvApi.CreateApplicationAsync(
            recruiterApi, AcmeId, vacancy.Id, externalCandidateId);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        return new Arranged(
            vacancy.Id,
            vacancy.Title,
            internalApplication.ApplicationId,
            internalApplication.CandidateId,
            employee.LastName,
            externalApplicationId,
            externalCandidateId,
            externalLastName);
    }

    private async Task<VacancyDetailPage> OpenApplicationsTabAsync(Arranged arranged)
    {
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await vacancyDetail.GoToAsync(AcmeId, arranged.VacancyId);
        await vacancyDetail.OpenApplicationsTabAsync();
        return vacancyDetail;
    }

    // ── 1. Applications tab badge ─────────────────────────────────────────────────

    [Fact]
    public async Task ApplicationsTab_ShowsInternalBadgeOnInternalRowOnly_BothInSamePipeline()
    {
        var arranged = await ArrangeAsync();
        var vacancyDetail = await OpenApplicationsTabAsync(arranged);

        await vacancyDetail.ExpectApplicationTypeFilterValueAsync("All");
        await vacancyDetail.ExpectApplicationRowTotalAsync(2);

        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.InternalApplicationId, isInternal: true);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.ExternalApplicationId, isInternal: false);

        // Same pipeline: both applications sit on the vacancy's initial stage.
        await vacancyDetail.ExpectApplicationStatusAsync(arranged.InternalLastName, InitialStage);
        await vacancyDetail.ExpectApplicationStatusAsync(arranged.ExternalLastName, InitialStage);
    }

    // ── 2. Applications tab filter ────────────────────────────────────────────────

    [Fact]
    public async Task ApplicationsTab_ApplicationTypeFilter_InternalExternalAndAllShowTheMatchingRows()
    {
        var arranged = await ArrangeAsync();
        var vacancyDetail = await OpenApplicationsTabAsync(arranged);

        await vacancyDetail.ExpectApplicationTypeFilterValueAsync("All");
        await vacancyDetail.ExpectApplicationRowTotalAsync(2);

        // Each transition changes the visible row set, so every expectation below can only be met by
        // the post-reload grid, never by the one it replaced.
        await vacancyDetail.SelectApplicationTypeFilterAsync("Internal");
        await vacancyDetail.ExpectApplicationTypeFilterValueAsync("Internal");
        await vacancyDetail.ExpectApplicationRowAbsentAsync(arranged.ExternalApplicationId);
        await vacancyDetail.ExpectApplicationRowTotalAsync(1);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.InternalApplicationId, isInternal: true);

        await vacancyDetail.SelectApplicationTypeFilterAsync("External");
        await vacancyDetail.ExpectApplicationTypeFilterValueAsync("External");
        await vacancyDetail.ExpectApplicationRowAbsentAsync(arranged.InternalApplicationId);
        await vacancyDetail.ExpectApplicationRowTotalAsync(1);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.ExternalApplicationId, isInternal: false);

        await vacancyDetail.SelectApplicationTypeFilterAsync("All");
        await vacancyDetail.ExpectApplicationTypeFilterValueAsync("All");
        await vacancyDetail.ExpectApplicationRowTotalAsync(2);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.InternalApplicationId, isInternal: true);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.ExternalApplicationId, isInternal: false);
    }

    // ── 3. Kanban card badge ──────────────────────────────────────────────────────

    [Fact]
    public async Task Kanban_InternalCardShowsBadge_ExternalCardDoesNot_BothInSameStageColumn()
    {
        var arranged = await ArrangeAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, arranged.VacancyId);

        await kanban.ExpectCardInternalBadgeAsync(arranged.InternalApplicationId, isInternal: true);
        await kanban.ExpectCardInternalBadgeAsync(arranged.ExternalApplicationId, isInternal: false);

        var (stageId, stageTitle) = await kanban.GetColumnOfCardAsync(arranged.InternalApplicationId);
        Assert.False(string.IsNullOrEmpty(stageId), "Expected the Kanban column to carry a data-stage-id.");
        Assert.Equal(InitialStage, stageTitle);
        await kanban.ExpectCardInStageColumnAsync(arranged.ExternalApplicationId, stageId);
    }

    // ── 4. Review CV badge ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewCv_InternalApplicationShowsBadgeBesideName_ExternalDoesNot()
    {
        var arranged = await ArrangeAsync();
        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);

        await review.GoToAsync(AcmeId, arranged.VacancyId, arranged.InternalApplicationId);
        await review.ExpectCandidateInternalBadgeAsync(isInternal: true);
        Assert.Contains(arranged.InternalLastName, await review.GetCandidateNameAsync() ?? "");
        // The name testid holds the name only — the badge text must not leak into it.
        Assert.DoesNotContain("Internal", await review.GetCandidateNameAsync() ?? "");

        await review.GoToAsync(AcmeId, arranged.VacancyId, arranged.ExternalApplicationId);
        Assert.Contains(arranged.ExternalLastName, await review.GetCandidateNameAsync() ?? "");
        await review.ExpectCandidateInternalBadgeAsync(isInternal: false);
    }

    // ── 5. Candidate detail ───────────────────────────────────────────────────────

    [Fact]
    public async Task CandidateDetail_InternalCandidate_ListsApplicationWithBadge_AndShowsInternalApplicantAlert()
    {
        var arranged = await ArrangeAsync();
        var candidate = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await candidate.GoToAsync(AcmeId, arranged.InternalCandidateId);

        await candidate.ExpectApplicationRowAsync(arranged.InternalApplicationId, arranged.VacancyTitle, isInternal: true);
        await candidate.ExpectApplicationRowCountAsync(1);
        await candidate.ExpectApplicationRowStageAsync(arranged.InternalApplicationId, InitialStage);

        // The internal applicant is linked to their employee record at apply time, but must be shown
        // as a current employee — not as "hired and linked".
        await candidate.ExpectInternalApplicantAlertAsync(visible: true);
        await candidate.ExpectNoHiredAlertAsync();
    }

    [Fact]
    public async Task CandidateDetail_ExternalCandidate_ApplicationRowHasNoBadge_AndNoInternalApplicantAlert()
    {
        var arranged = await ArrangeAsync();
        var candidate = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await candidate.GoToAsync(AcmeId, arranged.ExternalCandidateId);

        // Row first: proves the applications list has loaded before asserting the alert is absent.
        await candidate.ExpectApplicationRowAsync(arranged.ExternalApplicationId, arranged.VacancyTitle, isInternal: false);
        await candidate.ExpectApplicationRowCountAsync(1);
        await candidate.ExpectApplicationRowStageAsync(arranged.ExternalApplicationId, InitialStage);

        await candidate.ExpectInternalApplicantAlertAsync(visible: false);
        await candidate.ExpectNoHiredAlertAsync();
    }

    // ── 6. Recruitment Pipeline report — own vacancy row ──────────────────────────

    [Fact]
    public async Task RecruitmentPipelineReport_GroupedByVacancy_ApplicationFilterChangesOwnVacancyCandidateCount()
    {
        var arranged = await ArrangeAsync();

        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);

        // Group by Vacancy: the row for this test's uniquely titled vacancy only ever counts this
        // test's two applications, so its Candidates value is deterministic even though every other
        // row (and the company-wide totals) is shared with parallel tests. Values alternate
        // 2 → 1 → 2 → 1 so each expectation can only be met after the filter's reload.
        await report.SelectGroupByAsync("Vacancy");
        await report.ExpectVacancyRowCandidatesAsync(arranged.VacancyTitle, "2");

        await report.SelectApplicationTypeAsync(ReportApplicationTypeFilter.Internal);
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.Internal);
        await report.ExpectVacancyRowCandidatesAsync(arranged.VacancyTitle, "1");
        await report.ExpectRenderedWithoutErrorAsync();

        await report.SelectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);
        await report.ExpectVacancyRowCandidatesAsync(arranged.VacancyTitle, "2");

        await report.SelectApplicationTypeAsync(ReportApplicationTypeFilter.External);
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.External);
        await report.ExpectVacancyRowCandidatesAsync(arranged.VacancyTitle, "1");
        await report.ExpectRenderedWithoutErrorAsync();
    }
}
