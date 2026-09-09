using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the Department edit page
/// (DepartmentEdit.razor, /companies/{companyId}/departments/{id}), which renders the shared
/// &lt;SaveConflictBanner&gt; via EditPageBase. When a save is rejected with HTTP 409 because the
/// department's Version moved on since the form loaded it, the page renders the banner ("Someone
/// else changed this department while you were editing. Your changes have not been saved.") plus a
/// "Reload latest values" button. The page stays put and the first editor's entered values are
/// preserved until an explicit reload. "Reload latest values" re-fetches the department, repopulates
/// the form with the competing editor's values, adopts the fresh Version and clears the banner —
/// after which a re-save succeeds and navigates back to the list.
///
/// The "second editor" is a second tab in the same authenticated HR-admin context that loads the
/// same department and saves first, bumping its Version. Each test creates its own uniquely-named
/// department, so nothing here contends with seeded data or the other parallel test files —
/// deterministic at maxParallelThreads=15. The optional Description field is the mutated field (Name
/// is required; Description can be freely changed without tripping validation).
/// </summary>
public sealed class DepartmentEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DepartmentEdit_PageLoads_ShowsNameInForm()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var list     = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var edit     = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (name, id) = await CreateDepartmentAsync(list, edit);

        await edit.GoToAsync(AcmeId, id);

        Assert.Equal(name, await edit.GetNameAsync());
    }

    [Fact]
    public async Task DepartmentEdit_NormalEditAndSave_PersistsChange()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var list     = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var edit     = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, id)  = await CreateDepartmentAsync(list, edit);
        var newDesc  = $"E2E Desc {Guid.NewGuid():N}"[..20];

        await edit.GoToAsync(AcmeId, id);
        await edit.SetDescriptionAsync(newDesc);
        await edit.SaveAsync();

        await edit.GoToAsync(AcmeId, id);
        Assert.Equal(newDesc, await edit.WaitForDescriptionAsync(newDesc));
    }

    [Fact]
    public async Task DepartmentEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var list     = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var edit     = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, id) = await CreateDepartmentAsync(list, edit);

        var firstTabDesc = $"E2E First {Guid.NewGuid():N}"[..20];
        var otherTabDesc = $"E2E Other {Guid.NewGuid():N}"[..20];
        var finalDesc    = $"E2E Final {Guid.NewGuid():N}"[..20];

        // ── Tab 1: open the editor and start editing the Description (loads Version v1) ──
        await edit.GoToAsync(AcmeId, id);
        await edit.SetDescriptionAsync(firstTabDesc);

        // ── Tab 2 (same context / persona): load the same department and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEdit = new DepartmentEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToAsync(AcmeId, id);
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
        Assert.Contains($"/departments/{id}", _page.Url);
        Assert.Equal(firstTabDesc, await edit.GetDescriptionAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await edit.ClickReloadLatestValuesAsync();

        Assert.False(await edit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabDesc, await edit.WaitForDescriptionAsync(otherTabDesc));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await edit.SetDescriptionAsync(finalDesc);
        await edit.SaveAsync();

        await edit.GoToAsync(AcmeId, id);
        Assert.Equal(finalDesc, await edit.WaitForDescriptionAsync(finalDesc));
    }

    private async Task<(string Name, Guid Id)> CreateDepartmentAsync(DepartmentListPage list, DepartmentEditPage edit)
    {
        var name = $"E2E DeptConflict {Guid.NewGuid().ToString("N")[..8]}";

        await list.GoToAsync(AcmeId);
        await list.ClickNewDepartmentAsync();
        await edit.FillNameAsync(name);
        await edit.SaveAsync();

        Assert.True(await list.HasDepartmentAsync(name), $"Failed to seed department '{name}'");
        var href = await list.GetRowHrefAsync(name);
        return (name, Guid.Parse(href.Split('/').Last()));
    }
}
