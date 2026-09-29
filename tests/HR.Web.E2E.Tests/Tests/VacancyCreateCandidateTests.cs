using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the vacancy Applications tab's "Add Candidate" dialog modes (VacancyApplicationsTab.razor,
/// internal recruitment Ticket 3): "Create new candidate" (with/without a CV, with an External
/// Recruiter source), the duplicate-email follow-up (active → "Select existing candidate", inactive →
/// reactivate-first message), "Select existing candidate" with the "Attach current CV" option,
/// client-side validation, and the "already applied" guard.
///
/// Every test creates its own fresh Position Profile + published Vacancy via the UI (same flow as
/// CandidateCvDocumentsTests/CandidateCvReviewTests — a unique profile is required, only one live
/// vacancy per profile, and the Applications tab only renders for an Open vacancy), and seeds any
/// pre-existing candidate / CV / application / external recruiter through the real HR.Api via the
/// shared CandidateCvApi. All names, emails and file names are Guid-suffixed, so nothing depends on
/// shared seed state beyond Acme's recruitment pipeline.
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage is Recruiter-only. New applications land on the first
/// stage of Acme's shared, ordered pipeline ("Application Received"), which
/// RecruitmentStageManagementTests mutates.
/// </summary>
public sealed class VacancyCreateCandidateTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";

    private const string InitialStage = "Application Received";


    [Fact]
    public async Task NewCandidate_WithoutCv_IsAddedAndAppearsInGridOnInitialStage_WithoutLeavingVacancy()
    {
        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();
        var urlBefore = _page.Url;

        var unique = Unique();
        var lastName = $"NewCand{unique}";
        var email = $"e2e.newcand{unique}@example.com";

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();
        await dialog.FillNewCandidateAsync("E2E", lastName, email);
        await dialog.FillPhoneAsync("07700 900123");
        await dialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectActionSuccessMessageAsync(AddCandidateDialog.SuccessMessage);
        await vacancyDetail.ExpectApplicationRowCountAsync(lastName, 1);
        await vacancyDetail.ExpectApplicationStatusAsync(lastName, InitialStage);

        Assert.Equal(new Uri(urlBefore).AbsolutePath, new Uri(_page.Url).AbsolutePath);
        Assert.Contains(vacancyId.ToString(), _page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/candidates/", _page.Url, StringComparison.OrdinalIgnoreCase);

        using var api = await CreateRecruiterApiClientAsync();
        var apps = await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId);
        var app = Assert.Single(apps);
        Assert.Equal(lastName, app.CandidateLastName);
        Assert.Equal(email, app.CandidateEmail, ignoreCase: true);

        var snapshot = await CandidateCvApi.GetApplicationAsync(api, AcmeId, vacancyId, app.Id);
        Assert.Null(snapshot.CvDocumentId);
        Assert.Null(snapshot.CurrentCandidateCvDocumentId);
    }


    [Fact]
    public async Task NewCandidate_WithPdfCv_AttachesCvToApplicationAndAsCandidatesCurrentCv()
    {
        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var unique = Unique();
        var lastName = $"NewCv{unique}";
        var email = $"e2e.newcv{unique}@example.com";
        var fileName = $"e2e-new-candidate-cv-{Guid.NewGuid():N}.pdf";

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();
        await dialog.FillNewCandidateAsync("E2E", lastName, email);
        await dialog.SelectCvAsync(fileName, CandidateCvApi.BuildTestPdf());
        await dialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectActionSuccessMessageAsync(AddCandidateDialog.SuccessMessage);
        await vacancyDetail.ExpectApplicationRowCountAsync(lastName, 1);

        using var api = await CreateRecruiterApiClientAsync();
        var app = Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
        var snapshot = await CandidateCvApi.GetApplicationAsync(api, AcmeId, vacancyId, app.Id);

        Assert.NotNull(snapshot.CvDocumentId);
        Assert.Equal(fileName, snapshot.CvFileName);
        Assert.Equal(snapshot.CurrentCandidateCvDocumentId, snapshot.CvDocumentId);
        Assert.Equal(fileName, snapshot.CurrentCandidateCvFileName);
    }


    [Fact]
    public async Task NewCandidate_WithExternalRecruiterSource_ShowsSourceWithAgencyInGrid()
    {
        var unique = Unique();
        var agencyName = $"E2E NewCand Agency {unique}";

        using (var seedApi = await CreateRecruiterApiClientAsync())
        {
            await CandidateCvApi.CreateExternalRecruiterAsync(seedApi, AcmeId, agencyName);
        }

        var (_, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var lastName = $"AgencyCand{unique}";
        var email = $"e2e.agencycand{unique}@example.com";

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();
        await dialog.FillNewCandidateAsync("E2E", lastName, email);
        await dialog.SelectSourceAsync("External Recruiter");
        await dialog.SelectRecruiterAsync(agencyName);
        await dialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectApplicationRowCountAsync(lastName, 1);
        await vacancyDetail.ExpectApplicationSourceAsync(lastName, $"External Recruiter ({agencyName})");
    }


    [Fact]
    public async Task NewCandidate_DuplicateEmailDifferentCase_OffersSelectExisting_AndAddsExistingCandidateOnce()
    {
        var unique = Unique();
        var existingLast = $"DupCand{unique}";
        var existingName = $"E2E {existingLast}";
        var existingEmail = $"e2e.dupcand{unique}@example.com";

        Guid existingId;
        using (var seedApi = await CreateRecruiterApiClientAsync())
        {
            existingId = await CandidateCvApi.CreateCandidateAsync(seedApi, AcmeId, "E2E", existingLast, existingEmail);
        }

        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();
        await dialog.FillNewCandidateAsync("Someone", $"Else{unique}", existingEmail.ToUpperInvariant());
        await dialog.ClickSubmitAsync();

        await dialog.ExpectDuplicateAlertAsync(existingName, existingEmail);
        await dialog.ExpectSelectExistingButtonVisibleAsync();
        await dialog.ExpectOpenAsync();

        using var api = await CreateRecruiterApiClientAsync();
        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));

        await dialog.ClickSelectExistingCandidateAsync();
        await dialog.ExpectSelectedCandidateAsync(existingLast);
        await dialog.ExpectDuplicateAlertHiddenAsync();

        await dialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectActionSuccessMessageAsync(AddCandidateDialog.SuccessMessage);
        await vacancyDetail.ExpectApplicationRowCountAsync(existingLast, 1);
        await vacancyDetail.ExpectApplicationRowCountAsync($"Else{unique}", 0);

        // Exactly one application on this fresh vacancy, for the pre-existing candidate — no
        // duplicate candidate was created by the rejected new-candidate attempt.
        var app = Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
        Assert.Equal(existingId, app.CandidateId);
    }


    [Fact]
    public async Task NewCandidate_DuplicateEmailOfInactiveCandidate_ShowsInactiveMessage_WithoutSelectButton()
    {
        var unique = Unique();
        var inactiveLast = $"InactiveDup{unique}";
        var inactiveEmail = $"e2e.inactivedup{unique}@example.com";

        Guid inactiveId;
        using (var seedApi = await CreateRecruiterApiClientAsync())
        {
            inactiveId = await CandidateCvApi.CreateCandidateAsync(seedApi, AcmeId, "E2E", inactiveLast, inactiveEmail);
            await CandidateCvApi.DeactivateCandidateAsync(seedApi, AcmeId, inactiveId, "E2E inactive duplicate scenario");
        }

        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();
        await dialog.FillNewCandidateAsync("E2E", $"Other{unique}", inactiveEmail);
        await dialog.ClickSubmitAsync();

        await dialog.ExpectDuplicateAlertAsync($"E2E {inactiveLast}", inactiveEmail);
        await dialog.ExpectInactiveDuplicateAsync(inactiveId);
        await dialog.ExpectSelectExistingButtonAbsentAsync();
        await dialog.ExpectOpenAsync();

        using var api = await CreateRecruiterApiClientAsync();
        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
    }


    [Fact]
    public async Task ExistingCandidate_WithCurrentCv_AttachCheckboxDefaultsChecked_AndApplicationGetsThatCv()
    {
        var unique = Unique();
        var candidateLast = $"CurCv{unique}";
        var fileName = $"e2e-current-cv-{Guid.NewGuid():N}.pdf";

        Guid candidateId, cvDocumentId;
        using (var seedApi = await CreateRecruiterApiClientAsync())
        {
            candidateId = await CandidateCvApi.CreateCandidateAsync(
                seedApi, AcmeId, "E2E", candidateLast, $"e2e.curcv{unique}@example.com");
            cvDocumentId = await CandidateCvApi.UploadCandidateCvAsync(seedApi, AcmeId, candidateId, fileName);
        }

        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SelectExistingCandidateAsync(candidateLast);
        await dialog.ExpectAttachCurrentCvVisibleAsync(fileName);
        await dialog.ExpectAttachCurrentCvCheckedAsync();
        await dialog.SubmitExpectingSuccessAsync();

        await vacancyDetail.ExpectApplicationRowCountAsync(candidateLast, 1);

        using var api = await CreateRecruiterApiClientAsync();
        var app = Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
        Assert.Equal(candidateId, app.CandidateId);

        var snapshot = await CandidateCvApi.GetApplicationAsync(api, AcmeId, vacancyId, app.Id);
        Assert.Equal(cvDocumentId, snapshot.CvDocumentId);
        Assert.Equal(fileName, snapshot.CvFileName);
    }


    [Fact]
    public async Task NewCandidate_ClientValidation_RequiredFieldsWhitespaceInvalidEmailAndCvFileType()
    {
        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SwitchToNewModeAsync();

        await dialog.ClickSubmitAsync();
        await dialog.ExpectValidationMessageAsync("First name is required.");
        await dialog.ExpectValidationMessageAsync("Last name is required.");
        await dialog.ExpectValidationMessageAsync("Email is required.");
        await dialog.ExpectOpenAsync();

        await dialog.FillFirstNameAsync("   ");
        await dialog.FillLastNameAsync($"Valid{Unique()}");
        await dialog.FillEmailAsync("not-an-email");
        await dialog.ClickSubmitAsync();
        await dialog.ExpectValidationMessageAsync("First name is required.");
        await dialog.ExpectNoValidationMessageAsync("Last name is required.");
        await dialog.ExpectNoValidationMessageAsync("Email is required.");
        await dialog.ExpectValidationMessageAsync("Enter a valid email address.");
        await dialog.ExpectOpenAsync();

        // Unsupported CV type is rejected client-side and not kept as the selection.
        await dialog.SetCvFileAsync($"e2e-not-a-cv-{Guid.NewGuid():N}.txt", "plain text"u8.ToArray(), "text/plain");
        await dialog.ExpectCvErrorAsync("PDF, DOC or DOCX");
        await dialog.ExpectNoCvSelectedAsync();

        var pdfName = $"e2e-valid-cv-{Guid.NewGuid():N}.pdf";
        await dialog.SelectCvAsync(pdfName, CandidateCvApi.BuildTestPdf());
        await dialog.ExpectNoCvErrorAsync();
        await dialog.RemoveCvAsync();
        await dialog.ExpectNoCvSelectedAsync();

        await dialog.CancelAsync();

        using var api = await CreateRecruiterApiClientAsync();
        Assert.Empty(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
    }


    [Fact]
    public async Task ExistingCandidate_AlreadyAppliedToVacancy_ShowsAlreadyAppliedError()
    {
        var (vacancyId, vacancyDetail) = await ArrangePublishedVacancyOnApplicationsTabAsync();

        var unique = Unique();
        var candidateLast = $"Twice{unique}";

        using var api = await CreateRecruiterApiClientAsync();
        var candidateId = await CandidateCvApi.CreateCandidateAsync(
            api, AcmeId, "E2E", candidateLast, $"e2e.twice{unique}@example.com");
        await CandidateCvApi.CreateApplicationAsync(api, AcmeId, vacancyId, candidateId);

        await vacancyDetail.GoToAsync(AcmeId, vacancyId);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ExpectApplicationRowCountAsync(candidateLast, 1);

        var dialog = await OpenAddCandidateDialogAsync(vacancyDetail);
        await dialog.SelectExistingCandidateAsync(candidateLast);
        await dialog.ClickSubmitAsync();

        await dialog.ExpectGeneralErrorAsync("already applied");
        await dialog.ExpectOpenAsync();

        await dialog.CancelAsync();
        await vacancyDetail.ExpectApplicationRowCountAsync(candidateLast, 1);
        Assert.Single(await CandidateCvApi.ListApplicationsForVacancyAsync(api, AcmeId, vacancyId));
    }


    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private Task<HttpClient> CreateRecruiterApiClientAsync() =>
        CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

    private async Task<AddCandidateDialog> OpenAddCandidateDialogAsync(VacancyDetailPage vacancyDetail)
    {
        await vacancyDetail.ClickAddCandidateAsync();
        var dialog = new AddCandidateDialog(_page);
        await dialog.ExpectOpenAsync();
        await dialog.ExpectExistingModeAsync();
        return dialog;
    }

    private async Task<(Guid VacancyId, VacancyDetailPage VacancyDetail)> ArrangePublishedVacancyOnApplicationsTabAsync()
    {
        var vacancyTitle = $"E2E New Candidate Role {Unique()}";

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
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();

        var match = Regex.Match(_page.Url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{_page.Url}'.");
        return (Guid.Parse(match.Groups[1].Value), vacancyDetail);
    }
}
