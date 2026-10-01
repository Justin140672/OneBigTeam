using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Candidate profile "Applications" card (CandidateDetail.razor): each application links to its
/// vacancy (/view route with a returnUrl back to the candidate), keyboard activation, closed
/// vacancies stay linkable and read-only, the unsaved-changes guard runs when leaving a dirty
/// candidate through that link, and the returnUrl brings the user back.
///
/// Not covered: the "Vacancy unavailable" fallback (candidate-application-vacancy-unavailable) —
/// it only renders for an application whose VacancyId is empty or whose vacancy title is blank, and
/// no API in HR.Api can produce an application in that state, so it can't be seeded without faking.
/// </summary>
public sealed class RecruitmentNavigationTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string InitialStage = "Application Received";

    private static ILocator Row(IPage page, Guid applicationId) =>
        page.Locator($"[data-testid='candidate-application-row'][data-application-id='{applicationId}']");

    private async Task<CandidateEditPage> OpenCandidateAsync(Guid candidateId)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var edit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToAsync(AcmeId, candidateId);
        await Assertions.Expect(_page.GetByTestId("candidate-applications-card"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        return edit;
    }

    private Regex ViewHrefFor(Guid vacancyId, Guid candidateId) =>
        new($"^/companies/{AcmeId}/vacancies/{vacancyId}/view\\?returnUrl=%2Fcompanies%2F{AcmeId}%2Fcandidates%2F{candidateId}",
            RegexOptions.IgnoreCase);

    private Regex ViewUrlFor(Guid vacancyId) =>
        new($"/companies/{AcmeId}/vacancies/{vacancyId}/view", RegexOptions.IgnoreCase);

    [Fact]
    public async Task SingleApplication_ShowsVacancyLinkStageAndAppliedDate()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);

        await OpenCandidateAsync(candidate.Id);

        var row = Row(_page, applicationId);
        var link = row.GetByTestId("candidate-application-vacancy-link");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(link).ToHaveAttributeAsync("href", ViewHrefFor(vacancy.Id, candidate.Id));
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-label", new Regex("^View vacancy .+"));
        await Assertions.Expect(link).ToContainTextAsync(vacancy.Title[^8..]);

        await Assertions.Expect(row.GetByTestId("candidate-application-vacancy-unavailable")).ToHaveCountAsync(0);
        await Assertions.Expect(row.GetByTestId("candidate-application-stage")).ToHaveTextAsync(InitialStage);
        await Assertions.Expect(row.GetByTestId("candidate-application-applied"))
            .ToHaveTextAsync(new Regex(@"^\d{1,2} \w{3} \d{4}$"));
    }

    [Fact]
    public async Task VacancyLink_ActivatedWithKeyboard_NavigatesToVacancyViewPage()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);

        await OpenCandidateAsync(candidate.Id);

        var link = Row(_page, applicationId).GetByTestId("candidate-application-vacancy-link");
        await link.FocusAsync();
        await Assertions.Expect(link).ToBeFocusedAsync();
        await _page.Keyboard.PressAsync("Enter");

        await _page.WaitForURLAsync(ViewUrlFor(vacancy.Id), new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to vacancies", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task MultipleApplications_EachLinksToItsOwnVacancy()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var firstVacancy = await seed.CreateOpenVacancyAsync();
        var secondVacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var firstApplication = await seed.CreateApplicationAsync(firstVacancy.Id, candidate.Id);
        var secondApplication = await seed.CreateApplicationAsync(secondVacancy.Id, candidate.Id);

        await OpenCandidateAsync(candidate.Id);

        await Assertions.Expect(_page.GetByTestId("candidate-application-row")).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var firstLink = Row(_page, firstApplication).GetByTestId("candidate-application-vacancy-link");
        var secondLink = Row(_page, secondApplication).GetByTestId("candidate-application-vacancy-link");
        await Assertions.Expect(firstLink).ToHaveAttributeAsync("href", ViewHrefFor(firstVacancy.Id, candidate.Id));
        await Assertions.Expect(secondLink).ToHaveAttributeAsync("href", ViewHrefFor(secondVacancy.Id, candidate.Id));
        await Assertions.Expect(firstLink).ToContainTextAsync(firstVacancy.Title[^8..]);
        await Assertions.Expect(secondLink).ToContainTextAsync(secondVacancy.Title[^8..]);

        await secondLink.ClickAsync();
        await _page.WaitForURLAsync(ViewUrlFor(secondVacancy.Id), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ClosedVacancy_StaysLinkable_AndOpensReadOnly()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);
        await seed.CloseVacancyAsync(vacancy.Id);

        await OpenCandidateAsync(candidate.Id);

        var link = Row(_page, applicationId).GetByTestId("candidate-application-vacancy-link");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await link.ClickAsync();
        await _page.WaitForURLAsync(ViewUrlFor(vacancy.Id), new() { Timeout = 30_000 });

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to vacancies", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.Locator(".status-badge").First).ToHaveTextAsync("Closed");
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(_page.Locator("#vacancy-advert-title")).Not.ToBeEditableAsync();
        await Assertions.Expect(_page.Locator("#vacancy-advert-description")).Not.ToBeEditableAsync();
    }

    [Fact]
    public async Task DirtyCandidate_VacancyLink_ShowsUnsavedDialog_StayKeepsEditsAndUrl()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);

        var edit = await OpenCandidateAsync(candidate.Id);
        var candidateUrl = _page.Url;
        var editedFirstName = $"Edited{Guid.NewGuid().ToString("N")[..6]}";
        await edit.FillFirstNameAsync(editedFirstName);

        await Row(_page, applicationId).GetByTestId("candidate-application-vacancy-link").ClickAsync();

        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog when following a vacancy link from a dirty candidate.");
        var dialog = _page.Locator("[role='dialog']:has-text('Unsaved Changes')");
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page" })).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes" })).ToBeVisibleAsync();

        await edit.CancelUnsavedChangesDialogAsync();

        await Assertions.Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Equal(candidateUrl, _page.Url);
        Assert.Equal(editedFirstName, await edit.GetFirstNameAsync());
    }

    [Fact]
    public async Task DirtyCandidate_VacancyLink_DiscardChanges_NavigatesToVacancy()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);

        var edit = await OpenCandidateAsync(candidate.Id);
        await edit.FillFirstNameAsync($"Discard{Guid.NewGuid().ToString("N")[..6]}");

        await Row(_page, applicationId).GetByTestId("candidate-application-vacancy-link").ClickAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());

        await _page.Locator("[role='dialog']:has-text('Unsaved Changes')")
            .GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();

        await _page.WaitForURLAsync(ViewUrlFor(vacancy.Id), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task VacancyView_BackButton_ReturnsToCandidateProfile()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        var candidate = await seed.CreateCandidateAsync();
        var applicationId = await seed.CreateApplicationAsync(vacancy.Id, candidate.Id);

        await OpenCandidateAsync(candidate.Id);

        await Row(_page, applicationId).GetByTestId("candidate-application-vacancy-link").ClickAsync();
        await _page.WaitForURLAsync(ViewUrlFor(vacancy.Id), new() { Timeout = 30_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Back to vacancies", Exact = true }).ClickAsync();

        await _page.WaitForURLAsync(
            new Regex($"/companies/{AcmeId}/candidates/{candidate.Id}$", RegexOptions.IgnoreCase),
            new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByTestId("candidate-applications-card"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
