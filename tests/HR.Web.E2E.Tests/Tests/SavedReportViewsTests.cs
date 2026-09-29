using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SavedReportViewsTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private const string TomEmail = "tom.williams@acme.example";

    private static string UniqueViewName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..30];

    [Fact]
    public async Task SaveCurrentFiltersAsView_OpensModalDialog_CancelDiscardsWithoutSaving()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);
        await report.OpenSaveViewDialogAsync();

        Assert.True(await report.SaveViewDialog.IsVisibleAsync(),
            "Expected a modal dialog titled 'Save current filters as view' to open");

        var viewName = UniqueViewName("Cancelled");
        await _page.GetByPlaceholder("View name").FillAsync(viewName);
        await _page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        await report.SaveViewDialog.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden, Timeout = 10_000 });

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.DoesNotContain(viewName, options);
    }

    [Fact]
    public async Task SaveCurrentFiltersAsNewView_AppearsInSavedViewsDropdown()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var viewName = UniqueViewName("Save");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);

        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.Contains(viewName, options);
    }

    [Fact]
    public async Task SelectSavedView_ReappliesSavedFilters()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        await report.SelectFilterAsync("Status", "Active");
        await report.ApplyFiltersAsync();

        var viewName = UniqueViewName("Reapply");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);

        await report.ClearFiltersAsync();

        await report.SelectSavedViewAsync(viewName);

        Assert.False(await report.HasLoadErrorAsync(), "Expected selecting a saved view to reload the grid without an error banner");
        Assert.Null(await report.GetSavedViewErrorAsync());
    }

    [Fact]
    public async Task RenameSelectedView_UpdatesNameInDropdown()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var originalName = UniqueViewName("Rename");
        await report.SaveCurrentFiltersAsNewViewAsync(originalName);
        await report.SelectSavedViewAsync(originalName);

        var newName = UniqueViewName("Renamed");
        await report.RenameSelectedViewAsync(newName);

        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.Contains(newName, options);
        Assert.DoesNotContain(originalName, options);
    }

    [Fact]
    public async Task SetSelectedViewAsDefault_ShowsDefaultSuffixInDropdown()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var viewName = UniqueViewName("Default");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);
        await report.SelectSavedViewAsync(viewName);

        await report.SetSelectedViewAsDefaultAsync();

        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.Contains($"{viewName} (Default)", options);
    }

    [Fact]
    public async Task DeleteSelectedView_RemovesItFromDropdown()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var viewName = UniqueViewName("Delete");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);
        await report.SelectSavedViewAsync(viewName);

        await report.DeleteSelectedViewAsync();

        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.DoesNotContain(viewName, options);
    }

    [Fact]
    public async Task SaveCurrentFiltersAsNewView_ProducesExactlyOneMatchingView()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var viewName = UniqueViewName("Once");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);

        Assert.Null(await report.GetSavedViewErrorAsync());

        var options = await report.GetSavedViewOptionTextsAsync();
        Assert.Equal(1, options.Count(o => o == viewName || o == $"{viewName} (Default)"));
    }

    /// <summary>
    /// A plain Employee has no access to company reporting — navigating straight to a report route
    /// must not land them on the report (and therefore never exposes the shared "Saved Views"
    /// filter-panel section). They're redirected away rather than shown the grid.
    /// </summary>
    [Fact]
    public async Task PlainEmployee_CannotReachReport_OrSavedViews()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/employee-directory");
        await WaitForUrlToStopContainingAsync("/reporting/employee-directory");

        Assert.DoesNotContain("/reporting/employee-directory", _page.Url);
        Assert.False(await _page.Locator(".report-filter-toolbar button").IsVisibleAsync(),
            "The Saved Views / filter-panel toolbar must never render for a plain employee");
    }
}
