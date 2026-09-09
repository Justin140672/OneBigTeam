using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Optimistic-concurrency conflict UX on the external recruiter edit page
/// (ExternalRecruiterDetail.razor, /companies/{companyId}/external-recruiters/{id}), which now
/// renders the shared &lt;SaveConflictBanner&gt; via EditPageBase and treats an API 409 as a save
/// conflict. When a save is rejected with HTTP 409 because the recruiter's version moved on since
/// the form loaded it, the page renders the banner ("Someone else changed this external recruiter
/// while you were editing. Your changes have not been saved.") plus a "Reload latest values" button,
/// stays on the edit page, and preserves the first editor's entered values. "Reload latest values"
/// re-fetches the recruiter, repopulates the form with the competing editor's values, adopts the
/// fresh version and clears the banner — after which a re-save succeeds and navigates back to the
/// list.
///
/// The "second editor" is a second tab in the same authenticated Recruiter context. Each test
/// creates its own uniquely-named recruiter, so nothing here contends with seeded data or the other
/// parallel test files — deterministic at maxParallelThreads=15. The optional "Contact Name" field
/// is the mutated field — deliberately not "Agency Name", whose blur triggers the soft
/// duplicate-name check.
///
/// Uses Marcus Diallo (Recruiter role) — ExternalRecruiterDetail redirects non-Recruiters away.
/// </summary>
public sealed class ExternalRecruiterEditConcurrencyConflictTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task ExternalRecruiterEdit_PageLoads_ShowsRecruiterForm()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (name, id) = await CreateRecruiterAsync(recruiterList, recruiterEdit);

        await recruiterEdit.GoToAsync(AcmeId, id);

        Assert.Equal(name, await recruiterEdit.GetAgencyNameAsync());
    }

    [Fact]
    public async Task ExternalRecruiterEdit_NormalEditAndSave_PersistsChange()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateRecruiterAsync(recruiterList, recruiterEdit);
        var newContact = $"E2E Contact {Guid.NewGuid().ToString("N")[..8]}";

        await recruiterEdit.GoToAsync(AcmeId, id);
        await recruiterEdit.SetContactNameAsync(newContact);
        await recruiterEdit.SaveAsync();

        await recruiterEdit.GoToAsync(AcmeId, id);
        Assert.Equal(newContact, await recruiterEdit.WaitForContactNameAsync(newContact));
    }

    [Fact]
    public async Task ExternalRecruiterEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateRecruiterAsync(recruiterList, recruiterEdit);

        var firstTabContact = $"E2E Contact First {Guid.NewGuid().ToString("N")[..6]}";
        var otherTabContact = $"E2E Contact Other {Guid.NewGuid().ToString("N")[..6]}";
        var finalContact    = $"E2E Contact Final {Guid.NewGuid().ToString("N")[..6]}";

        // ── Tab 1: open the editor and start editing the Contact Name (loads version v1) ──
        await recruiterEdit.GoToAsync(AcmeId, id);
        await recruiterEdit.SetContactNameAsync(firstTabContact);

        // ── Tab 2 (same context / persona): load the same recruiter and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEdit = new ExternalRecruiterDetailPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToAsync(AcmeId, id);
            await otherEdit.SetContactNameAsync(otherTabContact);
            await otherEdit.SaveAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await recruiterEdit.SaveExpectingConflictAsync();

        Assert.True(await recruiterEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/external-recruiters/{id}", _page.Url);
        Assert.Equal(firstTabContact, await recruiterEdit.GetContactNameAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await recruiterEdit.ClickReloadLatestValuesAsync();

        Assert.False(await recruiterEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabContact, await recruiterEdit.WaitForContactNameAsync(otherTabContact));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await recruiterEdit.SetContactNameAsync(finalContact);
        await recruiterEdit.SaveAsync();

        await recruiterEdit.GoToAsync(AcmeId, id);
        Assert.Equal(finalContact, await recruiterEdit.WaitForContactNameAsync(finalContact));
    }

    /// <summary>Creates a uniquely-named external recruiter and returns its agency name and id.</summary>
    private async Task<(string Name, Guid Id)> CreateRecruiterAsync(
        ExternalRecruiterListPage recruiterList, ExternalRecruiterDetailPage recruiterEdit)
    {
        var name = $"E2E Agency Conflict {Guid.NewGuid().ToString("N")[..8]}";

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(name);
        await recruiterEdit.SaveAsync();

        Assert.True(await recruiterList.HasItemAsync(name), $"Failed to seed external recruiter '{name}'");
        await recruiterList.ClickRecruiterAsync(name);
        return (name, recruiterEdit.GetIdFromUrl());
    }
}
