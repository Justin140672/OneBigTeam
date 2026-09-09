using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the Document Type edit page
/// (DocumentTypeEdit.razor, /companies/{companyId}/document-types/{id}), which now renders the
/// shared &lt;SaveConflictBanner&gt; via EditPageBase. When a save is rejected with HTTP 409
/// because the document type's Version moved on since the form loaded it, the page renders the
/// banner ("Someone else changed this document type while you were editing. Your changes have not
/// been saved.") plus a "Reload latest values" button. The page stays put and the first editor's
/// entered values are preserved. "Reload latest values" re-fetches the document type, repopulates
/// the form with the competing editor's values, adopts the fresh Version and clears the banner —
/// after which a re-save succeeds and navigates back to the list.
///
/// The "second editor" is a second tab in the same authenticated HR-admin context that loads the
/// same document type and saves first, bumping its Version.
///
/// Each test creates its own uniquely-named document type, so nothing here contends with seeded
/// data or with the other parallel test files — deterministic at maxParallelThreads=15. The
/// optional Description field is used as the mutated field (Name is required; Description can be
/// freely blanked/changed without tripping validation).
/// </summary>
public sealed class DocumentTypeEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DocumentTypeEdit_PageLoads_ShowsNameInForm()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new DocumentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new DocumentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var name = await CreateDocumentTypeAsync(typeList, typeEdit);
        var id   = await GetDocumentTypeIdAsync(typeList, name);

        await typeEdit.GoToEditAsync(AcmeId, id);

        Assert.Equal(name, await typeEdit.GetNameAsync());
    }

    [Fact]
    public async Task DocumentTypeEdit_NormalEditAndSave_PersistsChange()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new DocumentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new DocumentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var name    = await CreateDocumentTypeAsync(typeList, typeEdit);
        var id      = await GetDocumentTypeIdAsync(typeList, name);
        var newDesc = $"E2E Desc {Guid.NewGuid():N}"[..20];

        await typeEdit.GoToEditAsync(AcmeId, id);
        await typeEdit.SetDescriptionAsync(newDesc);
        await typeEdit.SaveAsync();

        await typeEdit.GoToEditAsync(AcmeId, id);
        Assert.Equal(newDesc, await typeEdit.WaitForDescriptionAsync(newDesc));
    }

    [Fact]
    public async Task DocumentTypeEdit_SaveAfterAnotherActorChanged_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new DocumentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new DocumentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var name = await CreateDocumentTypeAsync(typeList, typeEdit);
        var id   = await GetDocumentTypeIdAsync(typeList, name);

        var firstTabDesc = $"E2E First {Guid.NewGuid():N}"[..20];
        var otherTabDesc = $"E2E Other {Guid.NewGuid():N}"[..20];
        var finalDesc    = $"E2E Final {Guid.NewGuid():N}"[..20];

        // ── Tab 1: open the editor and start editing the Description (loads Version v1) ──
        await typeEdit.GoToEditAsync(AcmeId, id);
        await typeEdit.SetDescriptionAsync(firstTabDesc);

        // ── Tab 2 (same context / persona): load the same document type and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEdit = new DocumentTypeEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToEditAsync(AcmeId, id);
            await otherEdit.SetDescriptionAsync(otherTabDesc);
            await otherEdit.SaveAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await typeEdit.SaveExpectingConflictAsync();

        Assert.True(await typeEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/document-types/{id}", _page.Url);
        Assert.Equal(firstTabDesc, await typeEdit.GetDescriptionAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await typeEdit.ClickReloadLatestValuesAsync();

        Assert.False(await typeEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabDesc, await typeEdit.WaitForDescriptionAsync(otherTabDesc));

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await typeEdit.SetDescriptionAsync(finalDesc);
        await typeEdit.SaveAsync();

        await typeEdit.GoToEditAsync(AcmeId, id);
        Assert.Equal(finalDesc, await typeEdit.WaitForDescriptionAsync(finalDesc));
    }

    /// <summary>Creates a uniquely-named document type via the list + new-page flow and returns its name.</summary>
    private async Task<string> CreateDocumentTypeAsync(DocumentTypeListPage typeList, DocumentTypeEditPage typeEdit)
    {
        var name = $"E2E DTConflict {Guid.NewGuid().ToString("N")[..8]}";

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(name);
        await typeEdit.SaveAsync();

        Assert.True(await typeList.HasItemAsync(name), $"Failed to seed document type '{name}'");
        return name;
    }

    private async Task<Guid> GetDocumentTypeIdAsync(DocumentTypeListPage typeList, string name)
    {
        await typeList.GoToAsync(AcmeId);
        var href = await typeList.GetRowHrefAsync(name);
        return Guid.Parse(href.Split('/').Last());
    }
}
