using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies Recruiter CRUD workflows for candidates:
/// - Seeded candidates appear in the list.
/// - A new candidate can be created and appears in the list.
/// - Validation errors surface when required fields are missing.
/// - Plain employees and HR Administrators (who lack the Recruiter role) cannot reach the
///   candidates page.
///
/// Uses Marcus Diallo (Recruiter role) rather than Laura Bennett (HR Administrator) — candidate:view
/// and recruitment:manage are Recruiter-only (see IdentityModule.AddRolePolicies); an HR
/// Administrator does not automatically get recruitment access.
/// </summary>
public sealed class CandidateManagementTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task CandidateList_ShowsSeededCandidates()
    {
        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);

        Assert.True(await candidateList.HasCandidateAsync("Emma Clarke"),
            "Expected 'Emma Clarke' in the candidate list");
        Assert.True(await candidateList.HasCandidateAsync("Liam Turner"),
            "Expected 'Liam Turner' in the candidate list");
        Assert.True(await candidateList.HasCandidateAsync("Noah Patel"),
            "Expected 'Noah Patel' in the candidate list");
        Assert.True(await candidateList.HasCandidateAsync("Olivia Grant"),
            "Expected 'Olivia Grant' in the candidate list");
    }

    [Fact]
    public async Task CreateCandidate_AppearsInList()
    {
        var unique       = Guid.NewGuid().ToString("N")[..8];
        var lastName     = $"E2ECand{unique}";
        var email        = $"e2e.cand{unique}@example.com";

        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
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

        Assert.True(await candidateList.HasCandidateAsync(lastName),
            $"Expected the new candidate '{lastName}' to appear in the list after creation");
    }

    [Fact]
    public async Task CreateCandidate_WithEmptyRequiredFields_ShowsValidationError()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateEdit.GoToNewAsync(AcmeId);

        // Leave all fields empty and try to save.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/candidates/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/candidates/new", _page.Url);
        Assert.True(await candidateEdit.HasErrorAsync(),
            "Expected a validation error when saving a candidate with no required fields filled in");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromCandidatesPage()
    {
        const string tomEmail = "tom.williams@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(tomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/candidates");
        // The redirect for an unauthorised plain employee is a client-side Blazor NavigateTo
        // (OnBeforeLoadAsync), not a full page navigation, so NetworkIdle after the initial GET
        // is not a reliable signal that the redirect has completed — poll the URL directly with a
        // generous timeout instead (avoids flakiness under heavy parallel load on shared personas).
        await WaitForUrlToStopContainingAsync("/candidates");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/candidates"),
            $"Expected a plain employee to be redirected away from the candidates page, but ended up at: {finalUrl}");
    }

    // HR Administrator (Laura) no longer holds the Recruiter role, so the candidates list page
    // guard (CandidateList.razor OnBeforeLoadAsync) redirects her away the same as a plain
    // employee, matching candidate:view being Recruiter-only at the API layer too (see
    // HrAdministrator_Gets_Forbidden_Listing_Candidates in RecruitmentAuthorizationTests).
    [Fact]
    public async Task HrAdministrator_IsRedirectedAway_FromCandidatesPage()
    {
        const string lauraEmail = "laura.bennett@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(lauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/candidates");
        // See WaitForUrlToStopContainingAsync's doc comment: the redirect is a client-side Blazor
        // NavigateTo (OnBeforeLoadAsync), not a full page navigation, so NetworkIdle after the
        // initial GET is not a reliable signal that the redirect has completed.
        await WaitForUrlToStopContainingAsync("/candidates");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/candidates"),
            $"Expected an HR Administrator without the Recruiter role to be redirected away from the candidates page, but ended up at: {finalUrl}");
    }
}
