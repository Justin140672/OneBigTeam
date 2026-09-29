using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "CV documents" card on the candidate details page (CandidateDetail.razor, internal
/// recruitment Ticket 2): first upload, replacement uploads retaining older CVs (newest first, the
/// "Current CV" badge only on the newest), and older CVs still referenced by an application showing
/// "Submitted with N application(s)".
///
/// Every test creates its own fresh candidate through the real HR.Api (POST .../candidates via the
/// shared CandidateCvApi, which is far cheaper than the Add Candidate UI and doesn't touch the
/// candidate grid), with unique names and file names. The application-referenced scenario also
/// creates its own fresh Position Profile + Vacancy via the UI (same flow as CandidateCvReviewTests)
/// and seeds the application/CV via the API, so no shared seeded vacancy is mutated.
///
/// Uses Marcus Diallo (Recruiter role) — uploads are gated on Session.CanManageRecruitment. Runs in parallel with the
/// other recruitment classes (it creates a vacancy, which relies on Acme's shared pipeline config).
/// </summary>
public sealed class CandidateCvDocumentsTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";

    [Fact]
    public async Task FirstUpload_FromCandidateDetails_ShowsSingleRowWithCurrentCvBadge()
    {
        using var api = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var candidateId = await CreateFreshCandidateAsync(api);

        var cvs = await OpenCandidateCvsAsync(candidateId);

        Assert.True(await cvs.IsEmptyStateVisibleAsync(), "Expected the empty state for a candidate with no CVs");
        await cvs.ExpectRowCountAsync(0);
        Assert.Equal(CandidateCvDocumentsSection.UploadButtonText, await cvs.GetUploadButtonTextAsync());
        Assert.False(await cvs.IsLegacyLinkVisibleAsync(), "No legacy link without a ResumeUrl");

        var fileName = $"e2e-first-cv-{Guid.NewGuid():N}.pdf";
        await cvs.UploadCvAsync(fileName, CandidateCvApi.BuildTestPdf());

        await cvs.ExpectRowCountAsync(1);
        Assert.Equal(new[] { fileName }, await cvs.GetRowFileNamesAsync());
        Assert.True(await cvs.RowHasCurrentBadgeAsync(fileName), "The only CV must carry the 'Current CV' badge");
        Assert.Equal(1, await cvs.GetCurrentBadgeCountAsync());
        Assert.False(await cvs.RowHasReferencedTextAsync(fileName), "A freshly uploaded CV isn't submitted with any application");
        Assert.False(await cvs.IsEmptyStateVisibleAsync());
        await cvs.ExpectUploadButtonTextAsync(CandidateCvDocumentsSection.UploadReplacementButtonText);

        cvs = await OpenCandidateCvsAsync(candidateId);
        await cvs.ExpectRowCountAsync(1);
        Assert.True(await cvs.RowHasCurrentBadgeAsync(fileName));
    }

    [Fact]
    public async Task Upload_ShowsScanState_AndEnablesDownloadOnlyOnceClean()
    {
        using var api = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var candidateId = await CreateFreshCandidateAsync(api);
        var cvs = await OpenCandidateCvsAsync(candidateId);

        var fileName = $"e2e-scan-cv-{Guid.NewGuid():N}.pdf";
        await cvs.UploadCvAsync(fileName, CandidateCvApi.BuildTestPdf());
        await cvs.ExpectRowCountAsync(1);

        var firstStatus = await cvs.GetRowScanStatusAsync(fileName);
        Assert.Contains(firstStatus, new[] { "Pending", "Scanning", "Clean" });
        if (firstStatus is "Pending" or "Scanning")
        {
            var disabled = await cvs.IsRowDownloadDisabledAsync(fileName);
            var enabled = await cvs.IsRowDownloadLinkEnabledAsync(fileName);
            Assert.True(disabled ^ enabled, "A row must show exactly one of: disabled file name, download link");
        }

        await cvs.WaitForRowScanStatusAsync(fileName, "Clean");

        Assert.True(await cvs.IsRowDownloadLinkEnabledAsync(fileName), "A Clean CV must be downloadable");
        Assert.False(await cvs.IsRowDownloadDisabledAsync(fileName));
        Assert.Contains($"/candidates/{candidateId}/cv/", await cvs.GetRowDownloadHrefAsync(fileName));
        Assert.Equal(new[] { fileName }, await cvs.GetRowFileNamesAsync());
    }

    [Fact]
    public async Task UploadReplacement_RetainsOlderCv_NewestFirst_CurrentBadgeOnlyOnNewest()
    {
        using var api = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var candidateId = await CreateFreshCandidateAsync(api);

        var cvs = await OpenCandidateCvsAsync(candidateId);

        var v1FileName = $"e2e-cv-v1-{Guid.NewGuid():N}.pdf";
        await cvs.UploadCvAsync(v1FileName, CandidateCvApi.BuildTestPdf());
        await cvs.ExpectRowCountAsync(1);
        await cvs.ExpectUploadButtonTextAsync(CandidateCvDocumentsSection.UploadReplacementButtonText);

        var v2FileName = $"e2e-cv-v2-{Guid.NewGuid():N}.pdf";
        await cvs.UploadCvAsync(v2FileName, CandidateCvApi.BuildTestPdf());

        await cvs.ExpectRowCountAsync(2);
        Assert.Equal(new[] { v2FileName, v1FileName }, await cvs.GetRowFileNamesAsync());
        Assert.True(await cvs.RowHasCurrentBadgeAsync(v2FileName), "The newest CV must be the 'Current CV'");
        Assert.False(await cvs.RowHasCurrentBadgeAsync(v1FileName), "The older CV must no longer be badged as current");
        Assert.Equal(1, await cvs.GetCurrentBadgeCountAsync());

        cvs = await OpenCandidateCvsAsync(candidateId);
        await cvs.ExpectRowCountAsync(2);
        Assert.Equal(new[] { v2FileName, v1FileName }, await cvs.GetRowFileNamesAsync());
        Assert.Equal(1, await cvs.GetCurrentBadgeCountAsync());
        Assert.True(await cvs.RowHasCurrentBadgeAsync(v2FileName));
    }

    [Fact]
    public async Task OlderCvReferencedByApplication_ShowsSubmittedWithCount_AndNoCurrentBadge()
    {
        using var api = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);
        var candidateId = await CreateFreshCandidateAsync(api);

        var vacancyId = await CreateFreshVacancyViaUiAsync();

        var v1FileName = $"e2e-cv-v1-{Guid.NewGuid():N}.pdf";
        var v1DocId = await CandidateCvApi.UploadCandidateCvAsync(api, AcmeId, candidateId, v1FileName);
        var applicationId = await CandidateCvApi.CreateApplicationAsync(api, AcmeId, vacancyId, candidateId);
        await CandidateCvApi.AttachCvToApplicationAsync(api, AcmeId, vacancyId, applicationId, v1DocId);

        var cvs = await OpenCandidateCvsAsync(candidateId);
        await cvs.ExpectRowCountAsync(1);
        await cvs.ExpectRowReferencedTextAsync(v1FileName, "Submitted with 1 application(s)");
        Assert.True(await cvs.RowHasCurrentBadgeAsync(v1FileName), "v1 is still the candidate's only (current) CV");

        var v2FileName = $"e2e-cv-v2-{Guid.NewGuid():N}.pdf";
        await cvs.UploadCvAsync(v2FileName, CandidateCvApi.BuildTestPdf());

        await cvs.ExpectRowCountAsync(2);
        Assert.Equal(new[] { v2FileName, v1FileName }, await cvs.GetRowFileNamesAsync());
        await cvs.ExpectRowReferencedTextAsync(v1FileName, "Submitted with 1 application(s)");
        Assert.False(await cvs.RowHasCurrentBadgeAsync(v1FileName), "The referenced older CV must not carry the 'Current CV' badge");
        Assert.True(await cvs.RowHasCurrentBadgeAsync(v2FileName));
        Assert.False(await cvs.RowHasReferencedTextAsync(v2FileName), "v2 was never submitted with an application");

        var app = await CandidateCvApi.GetApplicationAsync(api, AcmeId, vacancyId, applicationId);
        Assert.Equal(v1DocId, app.CvDocumentId);
        Assert.Equal(v2FileName, app.CurrentCandidateCvFileName);
    }


    private static async Task<Guid> CreateFreshCandidateAsync(HttpClient api)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        return await CandidateCvApi.CreateCandidateAsync(
            api, AcmeId, "E2E", $"CvDoc{unique}", $"e2e.cvdoc{unique}@example.com");
    }

    private async Task<CandidateCvDocumentsSection> OpenCandidateCvsAsync(Guid candidateId)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await candidateEdit.GoToAsync(AcmeId, candidateId);

        var cvs = new CandidateCvDocumentsSection(_page);
        await cvs.WaitForLoadedAsync();
        return cvs;
    }

    private async Task<Guid> CreateFreshVacancyViaUiAsync()
    {
        var unique       = Guid.NewGuid().ToString("N")[..8];
        var vacancyTitle = $"E2E CV Docs Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);

        var match = System.Text.RegularExpressions.Regex.Match(_page.Url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{_page.Url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }
}
