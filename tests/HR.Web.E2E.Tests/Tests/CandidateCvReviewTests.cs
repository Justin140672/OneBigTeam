using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Review CV" screen (ReviewCv.razor, ticket #1) and its two entry points — the vacancy
/// Kanban board candidate card menu (KanbanCandidateCard.razor's "Review CV" menu item) and the
/// per-row "Review CV" link on the Vacancy Detail Applications tab (VacancyApplicationsTab.razor).
///
/// Each test creates its own fresh Candidate + Position Profile + Vacancy + Application (unique names
/// per run) via the same flow as VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync, leaving the
/// application on the seeded initial stage ("Application Received"), rather than reusing the shared
/// seeded Acme applications (Emma Clarke et al.) — Move Forward / Reject mutate stage, and doing that
/// to shared seed data would race RecruitmentDashboardTests / RecruitmentPipelineReportTests etc.
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage is Recruiter-only. Runs in parallel with
/// RecruitmentStageManagementTests (no gate): Move Forward resolves "the next active non-terminal stage" from Acme's shared, ordered
/// recruitment pipeline config, which RecruitmentStageManagementTests mutates.
///
/// Internal recruitment Ticket 1 (CvPanel_* tests): the CV panel distinguishes the CV submitted with
/// the application from the candidate's current CV. Ticket 2 (ReplaceCv_* / UseCurrentCv_* /
/// CvPanel_UploadFromReviewPage_*): the Review CV page's application-scoped section uploads a CV and
/// records it as THIS application's CV only, or records the candidate's current CV against it.
/// Pre-existing CV state is seeded through the real HR.Api endpoints (candidate document upload with
/// Kind=Cv, then PUT .../applications/{id}/cv) via the shared CandidateCvApi using a Marcus session
/// from /api/dev/persona/{userId}. Every test uses its own fresh candidate/application(s) and unique
/// file names.
/// </summary>
public sealed class CandidateCvReviewTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";

    // RecruitmentStageSeeder.BuildDefaultStages — a freshly created Application starts on the first
    // (DisplayOrder 1); Move Forward advances it to the next active non-terminal stage.
    private const string InitialStage = "Application Received";
    private const string NextStage    = "CV Review";
    private const string RejectedStage = "Rejected";

    [Fact]
    public async Task ReviewCv_FromKanbanCardMenu_LoadsWithCandidateVacancyAndStage()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        Assert.Contains(arranged.CandidateLast, await review.GetCandidateNameAsync() ?? "");
        Assert.Equal(arranged.VacancyTitle, await review.GetPositionAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task ReviewCv_FromApplicationsTabRowLink_LoadsPage()
    {
        var arranged = await ArrangeApplicationAsync();

        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await vacancyDetail.GoToAsync(AcmeId, arranged.VacancyId);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickReviewCvForAsync(arranged.CandidateLast);

        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);
        await review.WaitForLoadedAsync();

        Assert.Contains(arranged.CandidateLast, await review.GetCandidateNameAsync() ?? "");
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task SaveNotes_PersistsAcrossReload_StageUnchanged()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var notes = $"Strong CV — relevant platform experience. {Guid.NewGuid():N}";
        await review.SetNotesAsync(notes);
        await review.SaveNotesAsync();
        Assert.True(await review.HasSuccessAlertAsync());

        var applicationId = review.GetApplicationIdFromUrl();

        // Fresh navigation → proves the notes were persisted server-side, not just left in local
        // component state.
        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(notes, await review.GetNotesAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());

        // Saving notes must not have moved the candidate on the board either.
        await arranged.Kanban.GoToStandaloneAsync(AcmeId, arranged.VacancyId);
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));
    }

    [Fact]
    public async Task MoveForward_AdvancesToNextStage_AndPersistsNotesEnteredBeforehand()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var notes = $"Progressing to CV Review. {Guid.NewGuid():N}";
        await review.SetNotesAsync(notes);

        var applicationId = review.GetApplicationIdFromUrl();

        await review.MoveForwardAsync();

        // Returned to the board (the returnUrl the card menu carried).
        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, NextStage),
            $"Expected the candidate to have moved to '{NextStage}' after Move Forward");
        Assert.False(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));

        // Notes typed before Move Forward are persisted as part of the same call.
        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(notes, await review.GetNotesAsync());
        Assert.Equal(NextStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task Reject_ViaDialog_MovesCandidateToRejectedStage()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        await review.RejectAsync("Not enough depth in distributed systems for this role.");

        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, RejectedStage),
            "Expected the candidate to land in the Rejected stage via the existing rejection workflow");
        Assert.False(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));
    }

    [Fact]
    public async Task Close_ReturnsToOrigin_WithoutChangingStageOrNotes()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var applicationId = review.GetApplicationIdFromUrl();

        // Type notes but DON'T save — Close must discard them and not move the candidate.
        await review.SetNotesAsync("Draft thoughts that should never be saved.");
        await review.CloseAsync();

        Assert.Contains("/kanban", _page.Url);

        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage),
            "Expected Close to leave the candidate on the original stage");

        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(string.Empty, await review.GetNotesAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    // ── Submitted CV vs candidate's current CV (internal recruitment Ticket 1) ─────

    [Fact]
    public async Task CvPanel_LegacyApplicationWithNoCvAtAll_ShowsPlainCvTitleNoBannerAndUploadSection()
    {
        // The Add Candidate dialog creates the application without a cvDocumentId and the fresh
        // candidate has no documents — the same shape as a historic/legacy application.
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        Assert.Equal(ReviewCvPage.NoCvTitle, await review.GetCvPanelTitleAsync());
        Assert.True(await review.IsNoCvMessageVisibleAsync(),
            "Expected 'No CV has been uploaded for this candidate.' when neither a submitted nor a current CV exists");
        Assert.False(await review.IsNoSubmittedCvBannerVisibleAsync(),
            "The 'No CV was captured with this application' banner must not show when there is no CV to show at all");
        Assert.False(await review.IsSubmittedFileNameVisibleAsync());
        Assert.False(await review.IsCurrentFileNameVisibleAsync());

        // Ticket 2: the application-scoped section offers an UPLOAD (not a replace) when the
        // application has no submitted CV, and there's no current CV to "use".
        Assert.True(await review.IsApplicationCvActionsVisibleAsync(),
            "Expected the application-scoped CV section to be offered on a non-withdrawn application");
        Assert.Equal(ReviewCvPage.UploadHeading, await review.GetReplaceHeadingAsync());
        Assert.Equal("Upload CV", await review.GetReplaceSubmitTextAsync());
        Assert.False(await review.IsUseCurrentCvVisibleAsync(),
            "'Use candidate's current CV' must not be offered when the candidate has no CV");
        Assert.False(await review.IsLegacyLinkVisibleAsync(),
            "The legacy CV link only shows when the candidate has a ResumeUrl");
    }

    [Fact]
    public async Task CvPanel_UploadFromReviewPage_AttachesCvToThisApplication()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);
        var applicationId = review.GetApplicationIdFromUrl();

        var fileName = $"e2e-app-cv-{Guid.NewGuid():N}.pdf";
        await review.ReplaceCvAsync(fileName, CandidateCvApi.BuildTestPdf(), ReviewCvPage.CvUploadedSuccess);

        await AssertShowsSubmittedCvAsync(review, fileName);

        // Fresh navigation → proves the state is what the server returns, not leftover component state.
        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        await AssertShowsSubmittedCvAsync(review, fileName);
        Assert.False(await review.IsUseCurrentCvVisibleAsync(),
            "The uploaded CV is both the application's CV and the candidate's current CV, so 'Use current' must not be offered");

        using var api = await CreateRecruiterApiClientAsync();
        var app = await GetApplicationAsync(api, arranged.VacancyId, applicationId);
        Assert.NotNull(app.CvDocumentId);
        Assert.Equal(app.CurrentCandidateCvDocumentId, app.CvDocumentId);
        Assert.Equal(fileName, app.CvFileName);
        Assert.Equal(fileName, app.CurrentCandidateCvFileName);
    }

    [Fact]
    public async Task ReplaceCv_UpdatesOnlyThisApplication_OtherApplicationKeepsOriginalCv()
    {
        // Application A and application B: the SAME candidate on two different fresh vacancies.
        var arrangedA = await ArrangeApplicationAsync();
        var reviewA = await OpenReviewCvFromKanbanAsync(arrangedA);
        var applicationAId = reviewA.GetApplicationIdFromUrl();

        var arrangedB = await ArrangeApplicationAsync(sameCandidateAs: arrangedA);
        var reviewB = await OpenReviewCvFromKanbanAsync(arrangedB);
        var applicationBId = reviewB.GetApplicationIdFromUrl();

        // Seed CV v1 as the submitted CV of BOTH applications via the real API.
        using var api = await CreateRecruiterApiClientAsync();
        var appA = await GetApplicationAsync(api, arrangedA.VacancyId, applicationAId);
        var appB = await GetApplicationAsync(api, arrangedB.VacancyId, applicationBId);
        Assert.Equal(appA.CandidateId, appB.CandidateId);

        var v1FileName = $"e2e-cv-v1-{Guid.NewGuid():N}.pdf";
        var v1DocId = await UploadCandidateCvAsync(api, appA.CandidateId, v1FileName);
        await SetApplicationCvAsync(api, arrangedA.VacancyId, applicationAId, v1DocId, appA.Version);
        await SetApplicationCvAsync(api, arrangedB.VacancyId, applicationBId, v1DocId, appB.Version);

        // Replace on A through the UI.
        await reviewA.GoToAsync(AcmeId, arrangedA.VacancyId, applicationAId);
        await AssertShowsSubmittedCvAsync(reviewA, v1FileName);
        Assert.Equal("Replace CV", await reviewA.GetReplaceSubmitTextAsync());

        var v2FileName = $"e2e-cv-v2-{Guid.NewGuid():N}.pdf";
        await reviewA.ReplaceCvAsync(v2FileName, CandidateCvApi.BuildTestPdf(), ReviewCvPage.CvReplacedSuccess);
        await AssertShowsSubmittedCvAsync(reviewA, v2FileName);

        // API: A now points at v2 (the candidate's new current CV); B still points at v1.
        var afterA = await GetApplicationAsync(api, arrangedA.VacancyId, applicationAId);
        Assert.NotNull(afterA.CvDocumentId);
        Assert.NotEqual(v1DocId, afterA.CvDocumentId);
        Assert.Equal(v2FileName, afterA.CvFileName);
        Assert.Equal(afterA.CurrentCandidateCvDocumentId, afterA.CvDocumentId);

        var afterB = await GetApplicationAsync(api, arrangedB.VacancyId, applicationBId);
        Assert.Equal(v1DocId, afterB.CvDocumentId);
        Assert.Equal(v1FileName, afterB.CvFileName);
        Assert.Equal(v2FileName, afterB.CurrentCandidateCvFileName);

        // UI: B still shows v1 as its Submitted CV, and — because the candidate's current CV (v2)
        // now differs from B's CV — offers "Use candidate's current CV".
        await reviewB.GoToAsync(AcmeId, arrangedB.VacancyId, applicationBId);
        await AssertShowsSubmittedCvAsync(reviewB, v1FileName);
        Assert.True(await reviewB.IsUseCurrentCvVisibleAsync(),
            "Expected 'Use candidate's current CV' on B once the candidate's current CV differs from B's CV");
    }

    [Fact]
    public async Task UseCurrentCv_AttachesCandidatesCurrentCv()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);
        var applicationId = review.GetApplicationIdFromUrl();

        // Candidate CV uploaded directly (not attached to the application).
        using var api = await CreateRecruiterApiClientAsync();
        var before = await GetApplicationAsync(api, arranged.VacancyId, applicationId);
        var fileName = $"e2e-current-cv-{Guid.NewGuid():N}.pdf";
        var currentDocId = await UploadCandidateCvAsync(api, before.CandidateId, fileName);

        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        await AssertShowsCurrentCvOnlyAsync(review, fileName);
        Assert.True(await review.IsUseCurrentCvVisibleAsync(),
            "Expected 'Use candidate's current CV for this application' when the application has no CV but the candidate has one");

        await review.UseCurrentCvAsync();

        await AssertShowsSubmittedCvAsync(review, fileName);
        Assert.False(await review.IsUseCurrentCvVisibleAsync(),
            "'Use current' must disappear once the current CV is the application's CV");

        var after = await GetApplicationAsync(api, arranged.VacancyId, applicationId);
        Assert.Equal(currentDocId, after.CvDocumentId);
    }

    [Fact]
    public async Task CvPanel_ApplicationWithSubmittedCv_ShowsSubmittedCv_AndKeepsItAfterNewerCandidateCvUploaded()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);
        var applicationId = review.GetApplicationIdFromUrl();

        // Seed through the real API: upload a Kind=Cv candidate document, then record it as the application's submitted CV with the
        // application's current Version as ExpectedVersion.
        using var api = await CreateRecruiterApiClientAsync();
        var before = await GetApplicationAsync(api, arranged.VacancyId, applicationId);
        Assert.Null(before.CvDocumentId);

        var submittedFileName = $"e2e-submitted-cv-{Guid.NewGuid():N}.pdf";
        var submittedDocId = await UploadCandidateCvAsync(api, before.CandidateId, submittedFileName);
        await SetApplicationCvAsync(api, arranged.VacancyId, applicationId, submittedDocId, before.Version);

        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        await AssertShowsSubmittedCvAsync(review, submittedFileName);

        // The candidate later uploads a newer CV — it becomes their CURRENT CV, but the application
        // must keep pointing at the exact CV submitted with it.
        var newerFileName = $"e2e-newer-cv-{Guid.NewGuid():N}.pdf";
        await UploadCandidateCvAsync(api, before.CandidateId, newerFileName);

        var after = await GetApplicationAsync(api, arranged.VacancyId, applicationId);
        Assert.Equal(submittedDocId, after.CvDocumentId);
        Assert.Equal(newerFileName, after.CurrentCandidateCvFileName);

        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        await AssertShowsSubmittedCvAsync(review, submittedFileName);
        Assert.NotEqual(newerFileName, await review.GetSubmittedFileNameAsync());
        Assert.True(await review.IsUseCurrentCvVisibleAsync(),
            "Expected 'Use candidate's current CV' once the candidate's current CV differs from the submitted CV");
    }

    private static async Task AssertShowsCurrentCvOnlyAsync(ReviewCvPage review, string expectedFileName)
    {
        await review.ExpectCvPanelTitleAsync(ReviewCvPage.CurrentCvTitle);
        Assert.True(await review.IsNoSubmittedCvBannerVisibleAsync(),
            "Expected the 'No CV was captured with this application' warning banner");
        Assert.Contains("No CV was captured with this application", await review.GetNoSubmittedCvBannerTextAsync() ?? "");
        Assert.True(await review.IsCurrentFileNameVisibleAsync());
        Assert.Equal(expectedFileName, await review.GetCurrentFileNameAsync());
        Assert.False(await review.IsSubmittedFileNameVisibleAsync(),
            "The candidate's current CV must never be presented as the submitted CV");
        Assert.Equal(ReviewCvPage.UploadHeading, await review.GetReplaceHeadingAsync());
    }

    private static async Task AssertShowsSubmittedCvAsync(ReviewCvPage review, string expectedFileName)
    {
        await review.ExpectCvPanelTitleAsync(ReviewCvPage.SubmittedCvTitle);
        Assert.True(await review.IsSubmittedFileNameVisibleAsync());
        Assert.Equal(expectedFileName, await review.GetSubmittedFileNameAsync());
        Assert.False(await review.IsNoSubmittedCvBannerVisibleAsync(),
            "The 'No CV was captured' banner must not show when the application has a submitted CV");
        Assert.False(await review.IsCurrentFileNameVisibleAsync());
        await review.ExpectReplaceHeadingAsync(ReviewCvPage.ReplaceHeading);
    }

    // ── API seeding helpers ─────────────────────────────────────────────────────
    // Thin wrappers over the shared CandidateCvApi: a real Marcus (Recruiter) session via
    // POST /api/dev/persona/{userId}, then direct calls to the same HR.Api endpoints the web app uses.

    private Task<HttpClient> CreateRecruiterApiClientAsync() =>
        CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

    private static Task<ApplicationCvSnapshot> GetApplicationAsync(HttpClient api, Guid vacancyId, Guid applicationId) =>
        CandidateCvApi.GetApplicationAsync(api, AcmeId, vacancyId, applicationId);

    private static Task<Guid> UploadCandidateCvAsync(HttpClient api, Guid candidateId, string fileName) =>
        CandidateCvApi.UploadCandidateCvAsync(api, AcmeId, candidateId, fileName);

    private static Task SetApplicationCvAsync(
        HttpClient api, Guid vacancyId, Guid applicationId, Guid cvDocumentId, int expectedVersion) =>
        CandidateCvApi.SetApplicationCvAsync(api, AcmeId, vacancyId, applicationId, cvDocumentId, expectedVersion);

    // ── Arrange helpers ─────────────────────────────────────────────────────────

    private sealed record ArrangedApplication(
        string CandidateLast, string CandidateName, string VacancyTitle, Guid VacancyId, VacancyKanbanBoardPage Kanban);

    /// <summary>
    /// Mirrors VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync: fresh candidate + position
    /// profile + published vacancy + application (left on <see cref="InitialStage"/>), browser left
    /// on the vacancy's standalone Kanban board. When <paramref name="sameCandidateAs"/> is given, the
    /// candidate is NOT created again — the new (fresh) vacancy's application is for that same
    /// candidate, so one candidate ends up with two applications on two vacancies.
    /// </summary>
    private async Task<ArrangedApplication> ArrangeApplicationAsync(ArrangedApplication? sameCandidateAs = null)
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = sameCandidateAs?.CandidateLast ?? $"Cv{unique}";
        var candidateName  = sameCandidateAs?.CandidateName ?? $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.cv{unique}@example.com";
        var vacancyTitle   = $"E2E CV Review Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        if (sameCandidateAs is null)
        {
            await candidateList.GoToAsync(AcmeId);
            await candidateList.ClickNewCandidateAsync();
            await candidateEdit.FillFirstNameAsync(candidateFirst);
            await candidateEdit.FillLastNameAsync(candidateLast);
            await candidateEdit.FillEmailAsync(candidateEmail);
            await candidateEdit.SaveNewCandidateAsync();
        }

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal(InitialStage, await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        var vacancyId = ExtractVacancyIdFromUrl(_page.Url);
        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);

        return new ArrangedApplication(candidateLast, candidateName, vacancyTitle, vacancyId, kanban);
    }

    private async Task<ReviewCvPage> OpenReviewCvFromKanbanAsync(ArrangedApplication arranged)
    {
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage),
            "Expected the freshly created application to start on the initial stage before opening Review CV");

        await arranged.Kanban.ClickReviewCvFromCardMenuAsync(arranged.CandidateLast);

        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);
        await review.WaitForLoadedAsync();
        return review;
    }

    private static Guid ExtractVacancyIdFromUrl(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }
}
