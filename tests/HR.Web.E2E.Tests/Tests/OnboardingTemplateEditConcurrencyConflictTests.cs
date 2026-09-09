using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the Onboarding Template edit page
/// (OnboardingTemplateEdit.razor, /companies/{companyId}/onboarding-templates/{id}), which renders
/// the shared &lt;SaveConflictBanner&gt;. OnboardingTemplateEdit does its own save via
/// OnboardingTemplateService.UpdateWithConcurrencyAsync and sets SaveConflict when the update comes
/// back as a concurrency conflict; ReloadServerStateAsync adopts the server's name/description/version
/// while deliberately leaving in-progress checklist-task edits untouched. When a save is rejected
/// because the template's Version moved on since the form loaded it, the page renders the banner
/// ("Someone else changed this onboarding template while you were editing. Your changes have not been
/// saved.") plus a "Reload latest values" button. The page stays put and the first editor's entered
/// values are preserved until an explicit reload — after which a re-save succeeds and navigates back
/// to the list.
///
/// The "second editor" is a second tab in the same authenticated HR-admin context that loads the
/// same template and saves first, bumping its Version. Each test creates its own uniquely-named
/// template with no checklist tasks, so nothing here contends with seeded data or the other parallel
/// test files — deterministic at maxParallelThreads=15. The optional Description field is the mutated
/// field (Name is required; Description can be freely changed without tripping validation).
/// </summary>
public sealed class OnboardingTemplateEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task OnboardingTemplateEdit_PageLoads_ShowsNameInForm()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var edit  = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (name, id) = await CreateTemplateAsync(list, edit);

        await edit.GoToEditAsync(AcmeId, id);

        Assert.Equal(name, await edit.GetNameAsync());
    }

    [Fact]
    public async Task OnboardingTemplateEdit_NormalEditAndSave_PersistsChange()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var edit  = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, id) = await CreateTemplateAsync(list, edit);
        var newDesc = $"E2E Desc {Guid.NewGuid():N}"[..20];

        await edit.GoToEditAsync(AcmeId, id);
        await edit.SetDescriptionAsync(newDesc);
        await edit.SaveAsync();

        await edit.GoToEditAsync(AcmeId, id);
        Assert.Equal(newDesc, await edit.WaitForDescriptionAsync(newDesc));
    }

    [Fact]
    public async Task OnboardingTemplateEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var edit  = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, id) = await CreateTemplateAsync(list, edit);

        var firstTabDesc = $"E2E First {Guid.NewGuid():N}"[..20];
        var otherTabDesc = $"E2E Other {Guid.NewGuid():N}"[..20];
        var finalDesc    = $"E2E Final {Guid.NewGuid():N}"[..20];

        // ── Tab 1: open the editor and start editing the Description (loads Version v1) ──
        await edit.GoToEditAsync(AcmeId, id);
        await edit.SetDescriptionAsync(firstTabDesc);

        // ── Tab 2 (same context / persona): load the same template and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEdit = new OnboardingTemplateEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToEditAsync(AcmeId, id);
            await otherEdit.SetDescriptionAsync(otherTabDesc);
            await otherEdit.SaveAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await edit.SaveExpectingConflictAsync();

        Assert.True(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/onboarding-templates/{id}", _page.Url);
        Assert.Equal(firstTabDesc, await edit.GetDescriptionAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await edit.ClickReloadLatestValuesAsync();

        Assert.False(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabDesc, await edit.WaitForDescriptionAsync(otherTabDesc));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await edit.SetDescriptionAsync(finalDesc);
        await edit.SaveAsync();

        await edit.GoToEditAsync(AcmeId, id);
        Assert.Equal(finalDesc, await edit.WaitForDescriptionAsync(finalDesc));
    }

    /// <summary>
    /// Regression for the two-part fix: <c>ReloadServerStateAsync</c> now also reloads
    /// <c>Model.Tasks</c> (the checklist) on a concurrency-conflict reload — previously the reload
    /// adopted the server's name/description/version but left the editor's stale task list in place,
    /// so the next save silently overwrote the other editor's checklist changes.
    ///
    /// Editor A and Editor B both open the same template (one checklist task). A edits the task
    /// title and saves. B edits the task title and saves → conflict banner. B clicks "Reload latest
    /// values" → B's checklist must now show A's task title (server state), not B's stale edit. B
    /// saves successfully and A's change survives. A further server-side change then makes B's next
    /// save conflict again (banner reappears).
    /// </summary>
    [Fact]
    public async Task OnboardingTemplateEdit_ReloadLatestValues_AdoptsServerChecklist_NotStaleTaskList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var edit  = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);   // Editor B

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, id) = await CreateTemplateAsync(list, edit);

        var originalTask = $"Task Orig {Guid.NewGuid():N}"[..18];
        var editorATask  = $"Task A {Guid.NewGuid():N}"[..18];
        var editorBTask  = $"Task B {Guid.NewGuid():N}"[..18];
        var editorBFinal = $"Task BF {Guid.NewGuid():N}"[..18];
        var serverDesc   = $"Srv Desc {Guid.NewGuid():N}"[..18];

        // Seed one checklist task on the template.
        await edit.GoToEditAsync(AcmeId, id);
        await edit.AddTaskWithTitleAsync(originalTask);
        await edit.SaveAsync();

        // ── Editor B: open the editor and start renaming the task (loads Version v1) ──
        await edit.GoToEditAsync(AcmeId, id);
        await edit.FillTaskTitleAsync(editorBTask);

        // ── Editor A (same context / persona, second tab): rename the task and save first ──
        var editorAPage = await _context.NewPageAsync();
        try
        {
            var editorA = new OnboardingTemplateEditPage(editorAPage, _fixture.WebBaseUrl);
            await editorA.GoToEditAsync(AcmeId, id);
            await editorA.FillTaskTitleAsync(editorATask);
            await editorA.SaveAsync();
        }
        finally
        {
            await editorAPage.CloseAsync();
        }

        // ── Editor B: saving now is stale → conflict banner, page stays put ──
        await edit.SaveExpectingConflictAsync();
        Assert.True(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the concurrency conflict banner after Editor A changed the checklist");
        Assert.Contains($"/onboarding-templates/{id}", _page.Url);

        // ── Editor B: "Reload latest values" must adopt Editor A's checklist, not B's stale edit ──
        await edit.ClickReloadLatestValuesAsync();
        Assert.False(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(editorATask, await edit.WaitForFirstTaskTitleAsync(editorATask));

        // ── Editor B: re-save against the fresh version succeeds; A's change survives ──
        await edit.SaveAsync();
        await edit.GoToEditAsync(AcmeId, id);
        Assert.Equal(editorATask, await edit.WaitForFirstTaskTitleAsync(editorATask));

        // ── A further server-side change makes Editor B's next save conflict again ──
        var serverPage = await _context.NewPageAsync();
        try
        {
            var serverEdit = new OnboardingTemplateEditPage(serverPage, _fixture.WebBaseUrl);
            await serverEdit.GoToEditAsync(AcmeId, id);
            await serverEdit.SetDescriptionAsync(serverDesc);
            await serverEdit.SaveAsync();
        }
        finally
        {
            await serverPage.CloseAsync();
        }

        await edit.FillTaskTitleAsync(editorBFinal);
        await edit.SaveExpectingConflictAsync();
        Assert.True(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to reappear after a further server-side change");
    }

    private async Task<(string Name, Guid Id)> CreateTemplateAsync(OnboardingTemplateListPage list, OnboardingTemplateEditPage edit)
    {
        var name = $"E2E OTConflict {Guid.NewGuid().ToString("N")[..8]}";

        await list.GoToAsync(AcmeId);
        await list.ClickNewAsync();
        await edit.FillNameAsync(name);
        await edit.SaveAsync();

        Assert.True(await list.HasItemAsync(name), $"Failed to seed onboarding template '{name}'");
        var href = await list.GetRowHrefAsync(name);
        return (name, Guid.Parse(href.Split('/').Last()));
    }
}
