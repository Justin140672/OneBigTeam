using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Add Candidate" dialog's Source/recruiter-picker fields (ticket #78,
/// VacancyApplicationsTab.razor): choosing Source="External Recruiter" reveals a recruiter
/// picker, the application is created successfully with that source recorded, and the
/// Applications grid's "Source" column reflects it.
///
/// Uses Marcus Diallo (Recruiter role), same reasoning as VacancyManagementTests/
/// ApplicationToEmployeeFlowTests. Creates a fresh candidate, vacancy and external recruiter each
/// run (unique names) to avoid colliding with previous runs' data.
/// </summary>
[Collection("E2E")]
public sealed class ApplicationSourceExternalRecruiterTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task AddCandidate_WithExternalRecruiterSource_CreatesApplication_AndShowsSourceColumn()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"SourceCand{unique}";
        var candidateEmail = $"e2e.sourcecand{unique}@example.com";
        var agencyName     = $"E2E Source Agency {unique}";
        var vacancyTitle   = $"E2E Source Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // ── Candidate ──────────────────────────────────────────────────────────────
        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        // ── Active external recruiter ────────────────────────────────────────────
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.SaveAsync();

        // ── Vacancy ───────────────────────────────────────────────────────────────
        // Assign the recruitment agency on the vacancy form itself (Vacancy.AssignedRecruiterId,
        // ticket #81) so picking it in the Add Candidate dialog below does NOT trigger the "not
        // currently assigned" warning (covered separately) — this replaced the old separate
        // "Recruiters" assignment tab, which no longer exists.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectRecruitmentAgencyAsync(agencyName);
        await vacancyDetail.SaveNewVacancyAsync();

        // ── Add the candidate with Source = External Recruiter ────────────────────
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateEmail);
        await vacancyDetail.SelectAddApplicationSourceAsync("External Recruiter");
        await vacancyDetail.SelectAddApplicationRecruiterAsync(agencyName);

        Assert.False(await vacancyDetail.IsRecruiterNotAssignedWarningVisibleAsync(),
            "Expected no 'not assigned' warning when the chosen recruiter IS currently assigned to this vacancy");

        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal("Applied", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        var sourceText = await vacancyDetail.GetApplicationSourceColumnTextAsync(candidateLast);
        Assert.NotNull(sourceText);
        Assert.Contains("External Recruiter", sourceText);
    }

    /// <summary>
    /// Risk/assumption: this exercises the non-blocking "not currently assigned" warning by
    /// picking a recruiter that has never been assigned to this fresh vacancy at all (rather than
    /// one whose assignment has since ended) — the warning's condition
    /// (VacancyApplicationsTab.razor's _vacancyAssignedRecruiterIds check) covers both cases
    /// identically, so this is a faithful, simpler trigger for the same code path.
    /// </summary>
    [Fact]
    public async Task AddCandidate_WithUnassignedExternalRecruiter_ShowsNonBlockingWarning_ButStillCreatesApplication()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"WarnCand{unique}";
        var candidateEmail = $"e2e.warncand{unique}@example.com";
        var agencyName     = $"E2E Unassigned Agency {unique}";
        var vacancyTitle   = $"E2E Warning Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
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

        // Active recruiter, but deliberately never assigned to the vacancy below.
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.SaveAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateEmail);
        await vacancyDetail.SelectAddApplicationSourceAsync("External Recruiter");
        await vacancyDetail.SelectAddApplicationRecruiterAsync(agencyName);

        Assert.True(await vacancyDetail.IsRecruiterNotAssignedWarningVisibleAsync(),
            "Expected the non-blocking 'not currently assigned' warning when the chosen recruiter has no assignment to this vacancy");

        // Advisory only — must not block submission.
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal("Applied", await vacancyDetail.GetApplicationStatusAsync(candidateLast));
    }
}
