using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Optimistic-concurrency conflict UX on the vacancy edit page (VacancyDetail.razor,
/// /companies/{companyId}/vacancies/{id}) — the EXISTING-vacancy branch only, not the new-vacancy
/// form. VacancyDetail now renders the shared &lt;SaveConflictBanner&gt; via EditPageBase and treats
/// an API 409 as a save conflict. When a save is rejected with HTTP 409 because the vacancy's
/// version moved on since the form loaded it, the page renders the banner ("Someone else changed
/// this vacancy while you were editing. Your changes have not been saved.") plus a "Reload latest
/// values" button, stays on the edit page, and preserves the first editor's entered values.
/// "Reload latest values" re-fetches the vacancy, repopulates the form with the competing editor's
/// values, adopts the fresh version and clears the banner — after which a re-save succeeds and
/// navigates back to the list.
///
/// The "second editor" is a second tab in the same authenticated Recruiter context. Each test
/// creates its own uniquely-titled Position Profile + vacancy, so nothing here contends with seeded
/// data or the other parallel test files — deterministic at maxParallelThreads=15. The optional
/// "Advert Title" field is the mutated field (it can be freely changed on an existing Draft vacancy
/// without tripping validation).
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage (vacancy creation/editing) is
/// Recruiter-only; the Position Profile helper briefly switches to Laura Bennett (HR admin) because
/// profile creation needs employee:manage, then switches back (see VacancyEditCloseBehaviorTests).
/// </summary>
public sealed class VacancyEditConcurrencyConflictTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";

    [Fact]
    public async Task VacancyEdit_PageLoads_ShowsExistingVacancyForm()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (title, id) = await CreateVacancyAsync(login, vacancyList, vacancyDetail);

        await vacancyDetail.GoToAsync(AcmeId, id);

        Assert.Equal(title, await vacancyDetail.WaitForAdvertTitleAsync(title));
    }

    [Fact]
    public async Task VacancyEdit_NormalEditAndSave_PersistsChange()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateVacancyAsync(login, vacancyList, vacancyDetail);
        var newTitle = $"E2E Vac Edited {Guid.NewGuid().ToString("N")[..8]}";

        await vacancyDetail.GoToAsync(AcmeId, id);
        await vacancyDetail.SetAdvertTitleAsync(newTitle);
        await vacancyDetail.SaveExistingVacancyAsync();

        await vacancyDetail.GoToAsync(AcmeId, id);
        Assert.Equal(newTitle, await vacancyDetail.WaitForAdvertTitleAsync(newTitle));
    }

    [Fact]
    public async Task VacancyEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (_, id) = await CreateVacancyAsync(login, vacancyList, vacancyDetail);

        var firstTabTitle = $"E2E Vac First {Guid.NewGuid().ToString("N")[..8]}";
        var otherTabTitle = $"E2E Vac Other {Guid.NewGuid().ToString("N")[..8]}";
        var finalTitle    = $"E2E Vac Final {Guid.NewGuid().ToString("N")[..8]}";

        // ── Tab 1: open the editor and start editing the Advert Title (loads version v1) ──
        await vacancyDetail.GoToAsync(AcmeId, id);
        await vacancyDetail.SetAdvertTitleAsync(firstTabTitle);

        // ── Tab 2 (same context / persona): load the same vacancy and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherDetail = new VacancyDetailPage(otherPage, _fixture.WebBaseUrl);
            await otherDetail.GoToAsync(AcmeId, id);
            await otherDetail.SetAdvertTitleAsync(otherTabTitle);
            await otherDetail.SaveExistingVacancyAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await vacancyDetail.SaveExpectingConflictAsync();

        Assert.True(await vacancyDetail.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/vacancies/{id}", _page.Url);
        Assert.Equal(firstTabTitle, await vacancyDetail.GetTitleAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await vacancyDetail.ClickReloadLatestValuesAsync();

        Assert.False(await vacancyDetail.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabTitle, await vacancyDetail.WaitForAdvertTitleAsync(otherTabTitle));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await vacancyDetail.SetAdvertTitleAsync(finalTitle);
        await vacancyDetail.SaveExistingVacancyAsync();

        await vacancyDetail.GoToAsync(AcmeId, id);
        Assert.Equal(finalTitle, await vacancyDetail.WaitForAdvertTitleAsync(finalTitle));
    }

    /// <summary>Creates a fresh Position Profile + Draft vacancy and returns the vacancy's advert title and id.</summary>
    private async Task<(string Title, Guid Id)> CreateVacancyAsync(
        LoginPage login, VacancyListPage vacancyList, VacancyDetailPage vacancyDetail)
    {
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        var title = $"E2E Vac Conflict {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(title);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(title);
        return (title, vacancyDetail.GetIdFromUrl());
    }
}
