using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the Public Holiday edit page
/// (PublicHolidayEdit.razor, /companies/{companyId}/public-holidays/{id}). When a save is rejected
/// with HTTP 409 because the holiday's Version moved on since the form loaded it, the page renders
/// the shared &lt;SaveConflictBanner&gt; ("Someone else changed this public holiday while you were
/// editing. Your changes have not been saved.") plus a "Reload latest values" button. The page
/// stays put and the first editor's entered values are preserved. "Reload latest values" re-fetches
/// the holiday, repopulates the form with the competing editor's values, adopts the fresh Version
/// and clears the banner — after which a re-save succeeds and navigates back to the list.
///
/// The "second editor" is a second tab in the same authenticated HR-admin context that loads the
/// same holiday and saves first, bumping its Version.
///
/// Each test creates its own uniquely-named holiday on a far-future date, so nothing here contends
/// with seeded data or with the ~other parallel test files — deterministic at maxParallelThreads=15.
/// </summary>
public sealed class PublicHolidayEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task PublicHolidayEdit_PageLoads_ShowsHolidayInForm()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var phList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var phEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var name = await CreateHolidayAsync(phList, phEdit, "12/03/2031");

        await phList.ClickHolidayAsync(name);

        Assert.Equal(name, await phEdit.GetNameAsync());
    }

    [Fact]
    public async Task PublicHolidayEdit_NormalEditAndSave_PersistsChange()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var phList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var phEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var name    = await CreateHolidayAsync(phList, phEdit, "13/03/2031");
        var newName = $"{name} Renamed";

        await phList.ClickHolidayAsync(name);
        await phEdit.FillNameAsync(newName);
        await phEdit.SaveAsync();

        Assert.True(await phList.HasHolidayAsync(newName),
            $"Expected the renamed holiday '{newName}' to appear in the list after a normal save");
    }

    [Fact]
    public async Task PublicHolidayEdit_SaveAfterAnotherActorChangedHoliday_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var phList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var phEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var baseName      = await CreateHolidayAsync(phList, phEdit, "14/03/2031");
        var firstTabName  = $"{baseName} FirstTab";
        var otherTabName  = $"{baseName} OtherTab";
        var finalName     = $"{baseName} Final";

        // ── Tab 1: open the holiday editor and start editing the Name (loads Version v1) ──
        await phList.ClickHolidayAsync(baseName);
        var holidayId = phEdit.CurrentHolidayId();
        await phEdit.FillNameAsync(firstTabName);

        // ── Tab 2 (same context / persona): load the same holiday and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherList = new PublicHolidayListPage(otherPage, _fixture.WebBaseUrl);
            var otherEdit = new PublicHolidayEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEdit.GoToEditAsync(AcmeId, holidayId);
            await otherEdit.FillNameAsync(otherTabName);
            await otherEdit.SaveAsync();
            Assert.True(await otherList.HasHolidayAsync(otherTabName));
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, input preserved ──
        await phEdit.SaveExpectingConflictAsync();

        Assert.True(await phEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.Contains($"/public-holidays/{holidayId}", _page.Url);
        Assert.Equal(firstTabName, await phEdit.GetNameAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await phEdit.ClickReloadLatestValuesAsync();

        Assert.False(await phEdit.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabName, await phEdit.GetNameAsync());

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await phEdit.FillNameAsync(finalName);
        await phEdit.SaveAsync();

        Assert.True(await phList.HasHolidayAsync(finalName),
            $"Expected '{finalName}' in the list after saving against the reloaded version");
    }

    /// <summary>Creates a uniquely-named holiday on the given far-future date and returns its name.</summary>
    private async Task<string> CreateHolidayAsync(
        PublicHolidayListPage phList, PublicHolidayEditPage phEdit, string ddMMyyyy)
    {
        var name = $"E2E PHConflict {Guid.NewGuid().ToString("N")[..8]}";

        await phList.GoToAsync(AcmeId);
        await phList.ClickNewPublicHolidayAsync();
        await phEdit.FillDateAsync(ddMMyyyy);
        await phEdit.FillNameAsync(name);
        await phEdit.FillCountryCodeAsync("GB");
        await phEdit.SaveAsync();

        Assert.True(await phList.HasHolidayAsync(name), $"Failed to seed holiday '{name}'");
        return name;
    }
}
