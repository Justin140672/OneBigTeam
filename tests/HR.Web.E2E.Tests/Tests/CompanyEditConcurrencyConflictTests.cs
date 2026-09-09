using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the Company profile edit page
/// (CompanyEdit.razor, /companies/{id}/edit). When a save is rejected with HTTP 409 because the
/// company's Version moved on since the form loaded it, the page renders the shared
/// &lt;SaveConflictBanner&gt; ("Someone else changed this company while you were editing. Your
/// changes have not been saved.") plus a "Reload latest values" button. The page stays put and the
/// first editor's entered values are preserved. "Reload latest values" re-fetches the company,
/// repopulates the form with the competing editor's values, adopts the fresh Version and clears
/// the banner — after which a re-save succeeds.
///
/// The "second editor" is a second tab in the same authenticated CompanyAdministrator context that
/// loads the same company and saves first, bumping the company's Version.
///
/// CompanyEdit's editable mode is gated on Session.CanManageCompany (company:manage →
/// CompanyAdministrator only), so this uses the Priya Shah CompanyAdmin persona — same as
/// CompanyAddressValidationTests / CompanyEditCloseBehaviorTests. There is only one seeded company
/// (Acme), so — exactly like those two sibling test classes — this exercises the first address
/// block's "Line 1" field and restores the seeded value in a finally block. The stale save never
/// persists, and the final value is put back, so the only cross-test window is between the second
/// tab's save and the finally-block restore; unique GUID-derived values keep assertions
/// unambiguous within this test.
/// </summary>
public sealed class CompanyEditConcurrencyConflictTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task CompanyEdit_PageLoads_ShowsProfileForm()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        Assert.False(string.IsNullOrWhiteSpace(await companyEdit.GetCompanyNameInputValueAsync()),
            "Expected the Company profile edit form to load with the company name populated");
    }

    [Fact]
    public async Task CompanyEdit_NormalEditAndSave_ShowsSuccessBanner()
    {
        var newLine1 = $"E2E Save {Guid.NewGuid():N}"[..20];

        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalLine1 = await companyEdit.GetFirstAddressLine1Async();
        try
        {
            await companyEdit.SetFirstAddressLine1Async(newLine1);
            await companyEdit.SaveExpectingSuccessAsync();

            Assert.True(await companyEdit.IsSaveSuccessVisibleAsync(),
                "Expected the inline success banner after a normal Company profile save");

            await companyEdit.GoToAsync(AcmeId);
            await companyEdit.OpenProfileTabAsync();
            Assert.Equal(newLine1, await companyEdit.GetFirstAddressLine1Async());
        }
        finally
        {
            await companyEdit.SetFirstAddressLine1Async(originalLine1);
            await companyEdit.SaveAsync();
        }
    }

    [Fact]
    public async Task CompanyEdit_SaveAfterAnotherActorChangedCompany_ShowsConflictBanner_ThenReloadRecovers()
    {
        var firstTabValue = $"E2E First {Guid.NewGuid():N}"[..20];
        var otherTabValue = $"E2E Other {Guid.NewGuid():N}"[..20];
        var finalValue    = $"E2E Final {Guid.NewGuid():N}"[..20];

        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();
        var originalLine1 = await companyEdit.GetFirstAddressLine1Async();

        try
        {
            // ── Tab 1: start editing the first address line (loads Version v1) ──
            await companyEdit.SetFirstAddressLine1Async(firstTabValue);

            // ── Tab 2 (same context / persona): load the same company and save first ──
            var otherPage = await _context.NewPageAsync();
            try
            {
                var otherEdit = new CompanyEditPage(otherPage, _fixture.WebBaseUrl);
                await otherEdit.GoToAsync(AcmeId);
                await otherEdit.OpenProfileTabAsync();
                await otherEdit.SetFirstAddressLine1Async(otherTabValue);
                await otherEdit.SaveExpectingSuccessAsync();
            }
            finally
            {
                await otherPage.CloseAsync();
            }

            // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
            await companyEdit.SaveExpectingConflictAsync();

            Assert.True(await companyEdit.IsConcurrencyWarningVisibleAsync(),
                "Expected the optimistic-concurrency conflict banner after a stale save");
            Assert.Contains("/edit", _page.Url);
            Assert.Equal(firstTabValue, await companyEdit.GetFirstAddressLine1Async());

            // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
            await companyEdit.ClickReloadLatestValuesAsync();

            Assert.False(await companyEdit.IsConcurrencyWarningVisibleAsync(),
                "Expected the conflict banner to clear after reloading latest values");
            Assert.Equal(otherTabValue, await companyEdit.GetFirstAddressLine1Async());

            // ── Tab 1: re-edit against the fresh version and save successfully ──
            await companyEdit.SetFirstAddressLine1Async(finalValue);
            await companyEdit.SaveExpectingSuccessAsync();

            Assert.True(await companyEdit.IsSaveSuccessVisibleAsync(),
                "Expected a success banner after saving against the reloaded version");
        }
        finally
        {
            await companyEdit.SetFirstAddressLine1Async(originalLine1);
            await companyEdit.SaveAsync();
        }
    }
}
