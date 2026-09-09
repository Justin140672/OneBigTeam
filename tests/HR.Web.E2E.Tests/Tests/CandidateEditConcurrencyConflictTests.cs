using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Optimistic-concurrency conflict UX on the candidate edit page (CandidateDetail.razor,
/// /companies/{companyId}/candidates/{id}), which now renders the shared &lt;SaveConflictBanner&gt;
/// via EditPageBase and treats an API 409 as a save conflict. When a save is rejected with HTTP 409
/// because the candidate's version moved on since the form loaded it, the page renders the banner
/// ("Someone else changed this candidate while you were editing. Your changes have not been saved.")
/// plus a "Reload latest values" button, stays on the edit page, and preserves the first editor's
/// entered values. "Reload latest values" re-fetches the candidate, repopulates the form with the
/// competing editor's values, adopts the fresh version and clears the banner — after which a re-save
/// succeeds.
///
/// The "second editor" is a second tab in the same authenticated Recruiter context that loads the
/// same candidate and saves first, bumping its version. Each test creates its own uniquely-named
/// candidate, so nothing here contends with seeded data or the other parallel test files —
/// deterministic at maxParallelThreads=15. The optional Phone field is the mutated field (First/Last
/// name and Email are required; Phone can be freely changed without tripping validation).
///
/// Uses Marcus Diallo (Recruiter role) — candidate:view / recruitment:manage are Recruiter-only
/// (see CandidateManagementTests' reasoning).
/// </summary>
public sealed class CandidateEditConcurrencyConflictTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task CandidateEdit_PageLoads_ShowsCandidateForm()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (lastName, id) = await CreateCandidateAsync(candidateList, candidateEdit);

        await candidateEdit.GoToAsync(AcmeId, id);

        Assert.Equal("E2E", await candidateEdit.GetFirstNameAsync());
    }

    [Fact]
    public async Task CandidateEdit_NormalEditAndSave_PersistsChange()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateCandidateAsync(candidateList, candidateEdit);
        var newPhone = $"07700 9{Guid.NewGuid().ToString("N")[..5]}";

        await candidateEdit.GoToAsync(AcmeId, id);
        await candidateEdit.SetPhoneAsync(newPhone);
        await candidateEdit.SaveAndWaitForListAsync();

        await candidateEdit.GoToAsync(AcmeId, id);
        Assert.Equal(newPhone, await candidateEdit.WaitForPhoneAsync(newPhone));
    }

    [Fact]
    public async Task CandidateEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateCandidateAsync(candidateList, candidateEdit);

        var firstTabPhone = $"07700 1{Guid.NewGuid().ToString("N")[..5]}";
        var otherTabPhone = $"07700 2{Guid.NewGuid().ToString("N")[..5]}";
        var finalPhone    = $"07700 3{Guid.NewGuid().ToString("N")[..5]}";

        // ── Tab 1: open the editor and start editing the Phone (loads version v1) ──
        await candidateEdit.GoToAsync(AcmeId, id);
        await candidateEdit.SetPhoneAsync(firstTabPhone);

        // ── Tab 2 (same context / persona): load the same candidate and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEdit = new CandidateEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToAsync(AcmeId, id);
            await otherEdit.SetPhoneAsync(otherTabPhone);
            await otherEdit.SaveAndWaitForListAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await candidateEdit.SaveExpectingConflictAsync();

        Assert.True(await candidateEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/candidates/{id}", _page.Url);
        Assert.Equal(firstTabPhone, await candidateEdit.GetPhoneAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await candidateEdit.ClickReloadLatestValuesAsync();

        Assert.False(await candidateEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabPhone, await candidateEdit.WaitForPhoneAsync(otherTabPhone));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await candidateEdit.SetPhoneAsync(finalPhone);
        await candidateEdit.SaveAndWaitForListAsync();

        await candidateEdit.GoToAsync(AcmeId, id);
        Assert.Equal(finalPhone, await candidateEdit.WaitForPhoneAsync(finalPhone));
    }

    /// <summary>Creates a uniquely-named candidate via the list + new-page flow and returns its last name and id.</summary>
    private async Task<(string LastName, Guid Id)> CreateCandidateAsync(
        CandidateListPage candidateList, CandidateEditPage candidateEdit)
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2ECandConflict{unique}";

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.cand.conflict{unique}@example.com");
        await candidateEdit.SaveNewCandidateAsync();

        Assert.True(await candidateList.HasCandidateAsync(lastName), $"Failed to seed candidate '{lastName}'");
        await candidateList.ClickCandidateAsync(lastName);
        return (lastName, candidateEdit.GetIdFromUrl());
    }
}
