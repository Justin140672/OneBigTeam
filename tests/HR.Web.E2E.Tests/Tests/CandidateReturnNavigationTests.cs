using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Add Candidate editor's return-navigation (CandidateDetail.razor's "?origin=" query
/// value, resolved via CandidateReturnDestination — src/HR.Web/Components/Pages/Recruitment/
/// CandidateReturnDestination.cs): Save and Close should send the recruiter back to whichever
/// screen launched the editor (Recruitment Dashboard or the Candidates list), and an
/// unrecognized/missing origin must safely fall back to the Candidates list rather than accepting
/// an arbitrary redirect target.
///
/// Uses Marcus Diallo (Recruiter role) — candidate:view/recruitment:manage (candidate creation)
/// are Recruiter-only, same persona used by CandidateEditCloseBehaviorTests and
/// RecruitmentDashboardRedesignTests.
/// </summary>
public sealed class CandidateReturnNavigationTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task LaunchedFromDashboard_AddCandidateButton_CarriesDashboardOrigin()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickAddCandidateAsync();

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/new"), new() { Timeout = 15_000 });
        Assert.Contains("origin=dashboard", _page.Url);
    }

    [Fact]
    public async Task LaunchedFromDashboard_Save_ReturnsToDashboard()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2EDashSave{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateEdit.GoToNewAsync(AcmeId, "dashboard");
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.dashsave{unique}@example.com");

        await candidateEdit.SaveNewCandidateAndWaitForUrlAsync("**/dashboard/recruitment");

        Assert.EndsWith("/dashboard/recruitment", _page.Url);
    }

    [Fact]
    public async Task LaunchedFromDashboard_Close_ReturnsToDashboard()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateEdit.GoToNewAsync(AcmeId, "dashboard");

        // No edits made yet, so Close should navigate straight back without the unsaved-changes
        // prompt appearing (mirrors CandidateEditCloseBehaviorTests.Close_ExistingRecordWithNoChanges).
        await candidateEdit.CloseAndWaitForUrlAsync("**/dashboard/recruitment");

        Assert.EndsWith("/dashboard/recruitment", _page.Url);
    }

    [Fact]
    public async Task LaunchedFromCandidatesList_AddCandidateButton_CarriesListOrigin()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/new"), new() { Timeout = 15_000 });
        Assert.Contains("origin=list", _page.Url);
    }

    [Fact]
    public async Task LaunchedFromCandidatesList_Save_ReturnsToCandidatesList()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2EListSave{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.listsave{unique}@example.com");

        await candidateEdit.SaveNewCandidateAndWaitForUrlAsync("**/candidates");

        Assert.EndsWith("/candidates", _page.Url);
        Assert.True(await candidateList.HasCandidateAsync(lastName),
            "Expected the newly created candidate to appear in the Candidates list after returning");
    }

    [Fact]
    public async Task MissingOrigin_Save_FallsBackToCandidatesList()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2ENoOriginSave{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // No "?origin=" at all — the original "launched from nowhere in particular" case.
        await candidateEdit.GoToNewAsync(AcmeId);
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.noorigin{unique}@example.com");

        await candidateEdit.SaveNewCandidateAndWaitForUrlAsync("**/candidates");

        Assert.EndsWith("/candidates", _page.Url);
    }

    [Fact]
    public async Task InvalidOrigin_Save_FallsBackToCandidatesList_NotToArbitraryUrl()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2EBadOriginSave{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // "origin" is resolved through a closed enum switch (CandidateReturnDestination) — an
        // unrecognized value (or an attempt to smuggle an arbitrary path/host through it) must be
        // treated the same as "no origin" and fall back to the Candidates list, never redirect
        // off to whatever string was supplied.
        await candidateEdit.GoToNewAsync(AcmeId, "https://evil.example.com");
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.badorigin{unique}@example.com");

        await candidateEdit.SaveNewCandidateAndWaitForUrlAsync("**/candidates");

        Assert.EndsWith("/candidates", _page.Url);
        Assert.DoesNotContain("evil.example.com", _page.Url);
    }

    [Fact]
    public async Task LaunchedFromDashboard_ValidationFailure_KeepsEditorOpen_WithEnteredValuesIntact()
    {
        var firstName = $"E2EDashInvalid{Guid.NewGuid():N}"[..20];

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateEdit.GoToNewAsync(AcmeId, "dashboard");

        // Fill in the first name but deliberately leave the other required fields (last name,
        // email) empty so Save fails validation — the editor must stay open (not redirect anywhere,
        // dashboard or otherwise) and the already-entered value must survive the failed attempt.
        await candidateEdit.FillFirstNameAsync(firstName);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/candidates/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/candidates/new", _page.Url);
        Assert.Contains("origin=dashboard", _page.Url);
        Assert.True(await candidateEdit.HasErrorAsync(),
            "Expected a validation error when saving a candidate with required fields missing");
        Assert.Equal(firstName, await candidateEdit.GetFirstNameAsync());
    }
}
