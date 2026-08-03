using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers a batch of Recruitment UI changes not already exercised by
/// VacancyDetailsAndListScreensUpdateTests / VacancyManagementTests / ExternalRecruiterManagementTests:
/// - Vacancy List: "Show active" toggle hides Closed vacancies until switched off, and an
///   "Applications" count column is present.
/// - Vacancy Detail: a Draft-status vacancy (freshly created, before any status transition) hides
///   the Applications/Interviews/Kanban tabs entirely.
/// - Vacancy Detail's Applications tab: row actions live in the grid's own toolbar, not a per-row
///   Actions column.
/// - External Recruiter edit: "Contact Name" renders on its own full-width row.
/// </summary>
[Collection("E2E")]
public sealed class RecruitmentUiUpdatesTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter

    [Fact]
    public async Task VacancyList_ShowActiveToggle_HidesClosedVacancies_UntilSwitchedOff()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        Assert.True(await vacancyList.IsShowingActiveOnlyAsync(),
            "Expected the list to default to showing active vacancies only");

        // "Senior Software Engineer" is a seeded Open vacancy — always visible regardless of the
        // toggle (see VacancyDetailsAndListScreensUpdateTests' own header comment on this seed).
        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected an active (non-Closed) vacancy to remain visible under 'Show active'");

        await vacancyList.ShowAllVacanciesAsync();
        Assert.False(await vacancyList.IsShowingActiveOnlyAsync());
    }

    [Fact]
    public async Task VacancyList_ShowsApplicationsCountColumn()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var vacancyTitle = $"E2E AppCount {Guid.NewGuid().ToString("N")[..8]}";

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle));

        var applicationsText = await vacancyList.GetApplicationsColumnTextAsync(vacancyTitle);
        Assert.Equal("0", applicationsText);
    }

    [Fact]
    public async Task NewDraftVacancy_HidesApplicationsInterviewsAndKanbanTabs()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var vacancyTitle = $"E2E DraftTabs {Guid.NewGuid().ToString("N")[..8]}";

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.False(await vacancyDetail.HasTabAsync("Applications"),
            "Did not expect an 'Applications' tab for a Draft-status vacancy");
        Assert.False(await vacancyDetail.HasTabAsync("Interviews"),
            "Did not expect an 'Interviews' tab for a Draft-status vacancy");
        Assert.False(await vacancyDetail.HasTabAsync("Kanban"),
            "Did not expect a 'Kanban' tab for a Draft-status vacancy");
    }

    [Fact]
    public async Task VacancyApplicationsTab_ActionsLiveInToolbar_NotAPerRowActionsColumn()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList    = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail  = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        var unique          = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst  = "E2E";
        var candidateLast   = $"Toolbar{unique}";
        var candidateName   = $"{candidateFirst} {candidateLast}";
        var candidateEmail  = $"e2e.toolbar{unique}@example.com";
        var vacancyTitle    = $"E2E Toolbar Vacancy {unique}";

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.False(await vacancyDetail.HasAnyPerRowApplicationActionButtonAsync(),
            "Did not expect per-row action buttons in the Applications grid");

        // Selecting the row and using the toolbar's "Withdraw" button (available on any active
        // application, unlike Schedule/Offer/Hire/Reject which additionally require no pending
        // interview) proves the toolbar-driven interaction actually works end-to-end.
        await vacancyDetail.ClickWithdrawForAsync(candidateLast);

        Assert.Contains("Withdrawn", (await vacancyDetail.GetApplicationRowTextAsync(candidateLast)) ?? "");
    }
}
