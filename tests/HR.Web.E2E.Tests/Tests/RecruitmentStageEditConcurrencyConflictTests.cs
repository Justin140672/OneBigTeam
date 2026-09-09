using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Optimistic-concurrency conflict UX on the recruitment stage edit page (RecruitmentStageEdit.razor,
/// /companies/{companyId}/recruitment-stages/{id}), which now renders the shared
/// &lt;SaveConflictBanner&gt; via EditPageBase and treats an API 409 as a save conflict. When a save
/// is rejected with HTTP 409 because the stage's version moved on since the form loaded it, the page
/// renders the banner ("Someone else changed this recruitment stage while you were editing. Your
/// changes have not been saved.") plus a "Reload latest values" button, stays on the edit page, and
/// preserves the first editor's entered values. "Reload latest values" re-fetches the stage,
/// repopulates the form with the competing editor's values, adopts the fresh version and clears the
/// banner — after which a re-save succeeds and navigates back to the list.
///
/// The "second editor" is a second tab in the same authenticated Recruiter context. Each test
/// creates its own uniquely-named non-terminal ("None" outcome) stage and mutates its Name; the
/// stage is deactivated in a finally block. Serializes against the CrossUserVacancyTestBase gate
/// exactly like RecruitmentStageManagementTests, because every test here mutates the single shared,
/// DisplayOrder-ranked list of Acme recruitment pipeline stages and a leftover active stage with the
/// highest DisplayOrder can hijack OfferCandidateHandler's stage selection for unrelated tests.
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage is Recruiter-only.
/// </summary>
public sealed class RecruitmentStageEditConcurrencyConflictTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    public override async Task InitializeAsync()
    {
        await CrossUserVacancyTestBase.GateInstance.WaitAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            CrossUserVacancyTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task RecruitmentStageEdit_PageLoads_ShowsStageForm()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (name, id) = await CreateStageAsync(stageList, stageEdit);
        try
        {
            await stageEdit.GoToAsync(AcmeId, id);
            Assert.Equal(name, await stageEdit.GetNameAsync());
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(name);
        }
    }

    [Fact]
    public async Task RecruitmentStageEdit_NormalEditAndSave_PersistsChange()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (name, id) = await CreateStageAsync(stageList, stageEdit);
        var updatedName = $"{name} Updated";

        try
        {
            await stageEdit.GoToAsync(AcmeId, id);
            await stageEdit.SetNameAsync(updatedName);
            await stageEdit.SaveAsync();

            await stageEdit.GoToAsync(AcmeId, id);
            Assert.Equal(updatedName, await stageEdit.WaitForNameAsync(updatedName));
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(updatedName);
        }
    }

    [Fact]
    public async Task RecruitmentStageEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var (name, id) = await CreateStageAsync(stageList, stageEdit);

        var firstTabName = $"{name} First";
        var otherTabName = $"{name} Other";
        var finalName    = $"{name} Final";

        try
        {
            // ── Tab 1: open the editor and start editing the Name (loads version v1) ──
            await stageEdit.GoToAsync(AcmeId, id);
            await stageEdit.SetNameAsync(firstTabName);

            // ── Tab 2 (same context / persona): load the same stage and save first ──
            var otherPage = await _context.NewPageAsync();
            try
            {
                var otherEdit = new RecruitmentStageEditPage(otherPage, _fixture.WebBaseUrl);
                await otherEdit.GoToAsync(AcmeId, id);
                await otherEdit.SetNameAsync(otherTabName);
                await otherEdit.SaveAsync();
            }
            finally
            {
                await otherPage.CloseAsync();
            }

            // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
            await stageEdit.SaveExpectingConflictAsync();

            Assert.True(await stageEdit.IsConcurrencyWarningVisibleAsync(),
                "Expected the optimistic-concurrency conflict banner after a stale save");
            Assert.Contains($"/recruitment-stages/{id}", _page.Url);
            Assert.Equal(firstTabName, await stageEdit.GetNameAsync());

            // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
            await stageEdit.ClickReloadLatestValuesAsync();

            Assert.False(await stageEdit.IsConcurrencyWarningVisibleAsync(),
                "Expected the conflict banner to clear after reloading latest values");
            Assert.Equal(otherTabName, await stageEdit.WaitForNameAsync(otherTabName));

            // ── Tab 1: re-edit against the fresh version and save successfully ──
            await stageEdit.SetNameAsync(finalName);
            await stageEdit.SaveAsync();

            await stageEdit.GoToAsync(AcmeId, id);
            Assert.Equal(finalName, await stageEdit.WaitForNameAsync(finalName));
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            foreach (var candidate in new[] { finalName, otherTabName, firstTabName, name })
            {
                if (await stageList.HasItemAsync(candidate))
                {
                    await stageList.DeactivateAsync(candidate);
                    break;
                }
            }
        }
    }

    /// <summary>Creates a uniquely-named non-terminal recruitment stage and returns its name and id.</summary>
    private async Task<(string Name, Guid Id)> CreateStageAsync(
        RecruitmentStageListPage stageList, RecruitmentStageEditPage stageEdit)
    {
        var name = $"E2E Stage Conflict {Guid.NewGuid().ToString("N")[..8]}";

        await stageList.GoToAsync(AcmeId);
        await stageList.ClickNewAsync();
        await stageEdit.FillNameAsync(name);
        // Leave Terminal Outcome at its default "None" — a plain non-terminal stage.
        await stageEdit.SaveAsync();

        Assert.True(await stageList.HasItemAsync(name), $"Failed to seed recruitment stage '{name}'");
        await stageList.ClickRowLinkAsync(name);
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
        return (name, stageEdit.GetIdFromUrl());
    }
}
