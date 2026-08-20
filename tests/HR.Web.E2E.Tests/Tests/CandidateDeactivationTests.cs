using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers candidate deactivation/reactivation (CandidateDetail.razor's Deactivate/Reactivate
/// buttons and dialogs, CandidateList.razor's "Show Inactive" toggle + Status column), added
/// alongside the recruitment candidate lifecycle work. Complements the CRUD coverage in
/// CandidateManagementTests, which this class intentionally does not duplicate (list/detail page
/// loads are already covered there).
///
/// Uses Marcus Diallo (Recruiter role), same persona/company as CandidateManagementTests and
/// VacancyKanbanBoardTests — candidate:manage/recruitment:manage are Recruiter-only.
/// </summary>
public sealed class CandidateDeactivationTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task DeactivateCandidate_WithoutReason_IsBlockedClientSide()
    {
        var (_, candidateLast, candidateList, candidateEdit) = await ArrangeActiveCandidateAsync();

        await candidateList.ClickCandidateAsync(candidateLast);
        await candidateEdit.ClickDeactivateAsync();

        Assert.True(await candidateEdit.IsDeactivateDialogVisibleAsync(),
            "Expected the deactivate-reason dialog to open");

        // Attempt to confirm with no reason entered.
        await candidateEdit.ClickConfirmDeactivateAsync();

        Assert.True(await candidateEdit.HasDeactivateReasonErrorAsync(),
            "Expected a client-side 'reason is required' validation message when confirming with an empty reason");
        Assert.True(await candidateEdit.IsDeactivateDialogVisibleAsync(),
            "Expected the deactivate dialog to remain open when no reason was entered");

        // Whitespace-only should be rejected the same way as empty (NotEmpty/IsNullOrWhiteSpace guard).
        await candidateEdit.FillDeactivateReasonAsync("   ");
        await candidateEdit.ClickConfirmDeactivateAsync();

        Assert.True(await candidateEdit.HasDeactivateReasonErrorAsync(),
            "Expected whitespace-only input to be treated the same as an empty reason");
        Assert.True(await candidateEdit.IsDeactivateDialogVisibleAsync(),
            "Expected the deactivate dialog to remain open for a whitespace-only reason");
    }

    [Fact]
    public async Task DeactivateCandidate_WithReason_ShowsInactiveBannerAndUpdatesList()
    {
        var (candidateId, candidateLast, candidateList, candidateEdit) = await ArrangeActiveCandidateAsync();
        var reason = $"E2E deactivation reason {Guid.NewGuid():N}"[..40];

        await candidateList.ClickCandidateAsync(candidateLast);
        await candidateEdit.ClickDeactivateAsync();
        await candidateEdit.FillDeactivateReasonAsync(reason);
        await candidateEdit.ConfirmDeactivateAndCloseAsync();

        Assert.True(await candidateEdit.HasInactiveBannerAsync(),
            "Expected the inactive banner to appear on the candidate detail page after deactivation");
        var bannerText = await candidateEdit.GetInactiveBannerTextAsync();
        Assert.Contains(reason, bannerText ?? string.Empty);

        // List view (after navigating back): excluded from the default active-only view, but
        // visible — and shown as Inactive — once "Show Inactive" is toggled on.
        await candidateList.GoToAsync(AcmeId);
        Assert.False(await candidateList.HasCandidateAsync(candidateLast),
            "Expected a deactivated candidate to be excluded from the default (active-only) list view");

        await candidateList.ShowInactiveAsync();
        Assert.True(await candidateList.HasCandidateAsync(candidateLast),
            "Expected the deactivated candidate to reappear once 'Show Inactive' is toggled on");
        Assert.False(await candidateList.IsActiveAsync(candidateLast),
            "Expected the deactivated candidate's Status column to show Inactive");
    }

    [Fact]
    public async Task ReactivateCandidate_RemovesInactiveBannerAndRestoresToActiveList()
    {
        var (_, candidateLast, candidateList, candidateEdit) = await ArrangeActiveCandidateAsync();
        var reason = $"E2E deactivation reason {Guid.NewGuid():N}"[..40];

        await candidateList.ClickCandidateAsync(candidateLast);
        await candidateEdit.ClickDeactivateAsync();
        await candidateEdit.FillDeactivateReasonAsync(reason);
        await candidateEdit.ConfirmDeactivateAndCloseAsync();
        Assert.True(await candidateEdit.HasInactiveBannerAsync());

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ShowInactiveAsync();
        await candidateList.ClickCandidateAsync(candidateLast);

        await candidateEdit.ClickReactivateAsync();
        Assert.True(await candidateEdit.IsReactivateDialogVisibleAsync(),
            "Expected the reactivate confirmation dialog to open");
        await candidateEdit.ConfirmReactivateAsync();

        Assert.False(await candidateEdit.HasInactiveBannerAsync(),
            "Expected the inactive banner to disappear once the candidate is reactivated");

        await candidateList.GoToAsync(AcmeId);
        Assert.True(await candidateList.HasCandidateAsync(candidateLast),
            "Expected a reactivated candidate to reappear in the default active list view");
        Assert.True(await candidateList.IsActiveAsync(candidateLast),
            "Expected a reactivated candidate's Status column to show Active");
    }

    /// <summary>
    /// A candidate with an unresolved (non-withdrawn, non-terminal-stage) active application
    /// cannot be deactivated — the server rejects with 422 and CandidateDetail.razor surfaces the
    /// API's error message in the action-error alert rather than silently failing.
    /// </summary>
    [Fact]
    public async Task DeactivateCandidate_WithUnresolvedActiveApplication_ShowsServerError()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"DeactBlock{unique}";
        var candidateName  = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.deactblock{unique}@example.com";
        var vacancyTitle   = $"E2E Deact Block Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
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

        // Position Profile is mandatory for creation — "Senior Software Engineer" is seeded for Acme
        // (same setup as VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync).
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

        // The application now sits on the seeded initial (non-terminal) stage — this is the
        // "unresolved active application" that should block deactivation.
        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickCandidateAsync(candidateLast);

        await candidateEdit.ClickDeactivateAsync();
        await candidateEdit.FillDeactivateReasonAsync("Attempting to deactivate with an active application");
        await candidateEdit.ClickConfirmDeactivateAsync();

        var errorText = await candidateEdit.GetActionErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(errorText),
            "Expected the server's rejection error to be shown in the action-error alert, not silently swallowed");

        // The candidate must remain active — the block was effective, not merely displayed.
        Assert.False(await candidateEdit.HasInactiveBannerAsync(),
            "Expected the candidate to remain active after a blocked deactivation attempt");
    }

    private async Task<(Guid CandidateId, string CandidateLast, CandidateListPage List, CandidateEditPage Edit)> ArrangeActiveCandidateAsync()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"E2EDeact{unique}";
        var email     = $"e2e.deact{unique}@example.com";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync(email);
        await candidateEdit.SaveNewCandidateAsync();

        // Id isn't surfaced through the list page object; callers that need it can extend this
        // later if a test requires it directly (none currently do — navigation is by name).
        return (Guid.Empty, lastName, candidateList, candidateEdit);
    }
}
