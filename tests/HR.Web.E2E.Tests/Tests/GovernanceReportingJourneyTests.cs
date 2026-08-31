using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-08 administrative governance reporting hub — per-report journeys for an HR Administrator,
/// covering the User Activity audit report (shared GovernanceAuditReport.razor) and the standalone
/// Compliance Status report:
///   - load the page, apply a filter (Status "Failed" / Severity "Overdue") and see the grid respond
///     without erroring (filtering a governance report can only narrow, never grow, the row set),
///   - export CSV and assert a download is produced,
///   - favourite the report, reload, and see the star stays lit (server round-trip via
///     ReportingService.Add/RemoveReportFavouriteAsync, same as the report catalogue favourites),
///   - save a personal report view from the current filters and re-apply it.
///
/// Runs serialized with the other files that toggle Laura Bennett's shared, server-persisted report
/// favourites (HrFavouritesSerialTestBase — see GroupSerializedTestBases.cs); the favourite test
/// self-heals a possibly-polluted starting state and cleans up in a finally, matching
/// ReportCatalogTests' conventions.
/// </summary>
public sealed class GovernanceReportingJourneyTests(HrAdminPersonaFixture fixture)
    : HrFavouritesSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    private static string UniqueViewName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    // ── User Activity (shared GovernanceAuditReport) ───────────────────────────

    [Fact]
    public async Task UserActivity_ApplyFailedStatusFilter_GridRespondsWithoutError()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = GovernanceAuditReportPage.UserActivity(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);
        var before = await report.GetRowCountAsync();

        await report.SelectStatusAsync("Failed");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying the Status filter");
        Assert.Null(await report.GetFilterErrorAsync());

        var after = await report.GetRowCountAsync();
        Assert.True(after <= before, $"Status filter increased the row count ({before} -> {after})");
    }

    [Fact]
    public async Task UserActivity_ExportCsv_ProducesADownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = GovernanceAuditReportPage.UserActivity(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var download = await report.ExportAsync("CSV");

        Assert.False(string.IsNullOrWhiteSpace(download.SuggestedFilename),
            "Expected the CSV export to trigger a browser download with a filename");
        Assert.Null(await report.GetExportErrorAsync());
    }

    [Fact]
    public async Task UserActivity_Favourite_PersistsAcrossReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = GovernanceAuditReportPage.UserActivity(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // Self-heal a possibly-polluted starting state on the shared, long-lived E2E database.
        if (await report.IsFavouritedAsync())
            await report.ClickFavouriteAsync();
        Assert.False(await report.IsFavouritedAsync());

        try
        {
            await report.ClickFavouriteAsync();
            Assert.True(await report.IsFavouritedAsync());

            await report.ReloadAsync();

            Assert.True(await report.IsFavouritedAsync(),
                "Expected the governance report favourite to survive a full page reload");
        }
        finally
        {
            await report.GoToAsync(AcmeId);
            if (await report.IsFavouritedAsync())
                await report.ClickFavouriteAsync();
        }

        Assert.False(await report.IsFavouritedAsync());
    }

    [Fact]
    public async Task UserActivity_SavePersonalView_ThenReapplyIt()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = GovernanceAuditReportPage.UserActivity(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // Establish a distinguishing filter before saving the view.
        await report.SelectStatusAsync("Failed");
        await report.ApplyFiltersAsync();

        var viewName = UniqueViewName("Gov");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);
        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.Contains(viewName, options);

        // Reset to defaults, then re-apply the saved view and confirm no error surfaces.
        await report.ClearFiltersAsync();
        await report.SelectSavedViewAsync(viewName);

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected re-applying the saved view to reload the grid without an error banner");
        Assert.Null(await report.GetSavedViewErrorAsync());
    }

    // ── Compliance Status (standalone) ────────────────────────────────────────

    [Fact]
    public async Task ComplianceStatus_ApplySeverityFilter_GridRespondsWithoutError()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new GovernanceComplianceStatusReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);
        var before = await report.GetRowCountAsync();

        await report.SelectSeverityAsync("Overdue");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying the Severity filter");

        var after = await report.GetRowCountAsync();
        Assert.True(after <= before, $"Severity filter increased the row count ({before} -> {after})");
    }

    [Fact]
    public async Task ComplianceStatus_ExportCsv_ProducesADownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new GovernanceComplianceStatusReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var download = await report.ExportAsync("CSV");

        Assert.False(string.IsNullOrWhiteSpace(download.SuggestedFilename),
            "Expected the CSV export to trigger a browser download with a filename");
        Assert.Null(await report.GetExportErrorAsync());
    }

    [Fact]
    public async Task ComplianceStatus_Favourite_PersistsAcrossReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new GovernanceComplianceStatusReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        if (await report.IsFavouritedAsync())
            await report.ClickFavouriteAsync();
        Assert.False(await report.IsFavouritedAsync());

        try
        {
            await report.ClickFavouriteAsync();
            Assert.True(await report.IsFavouritedAsync());

            await report.ReloadAsync();

            Assert.True(await report.IsFavouritedAsync(),
                "Expected the compliance-status favourite to survive a full page reload");
        }
        finally
        {
            await report.GoToAsync(AcmeId);
            if (await report.IsFavouritedAsync())
                await report.ClickFavouriteAsync();
        }

        Assert.False(await report.IsFavouritedAsync());
    }
}
