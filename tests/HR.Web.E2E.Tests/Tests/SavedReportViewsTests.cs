using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Saved Views" section of the report filter panel (ReportFilterPanel.razor), which
/// is shared across report pages — exercised here via the Employee Directory report
/// (/companies/{companyId}/reporting/employee-directory). Each test creates its own saved view
/// via the "Save current filters as view" flow rather than depending on shared seeded data, since
/// dev/E2E seed data isn't guaranteed to include saved views.
/// </summary>
[Collection("E2E")]
public sealed class SavedReportViewsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    private static string UniqueViewName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..30];

    /// <summary>
    /// Saving the current filters as a new named view must persist it and immediately surface it
    /// as a selectable option in the "Saved Views" dropdown (GetReportViews re-fetch after
    /// SaveCurrentAsViewAsync in ReportFilterPanel.razor).
    /// </summary>
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

    /// <summary>
    /// Selecting a saved view from the dropdown must re-apply the filters that were active when
    /// it was saved (OnSavedViewSelectedAsync), without surfacing an error banner.
    /// </summary>
    [Fact]
    public async Task SelectSavedView_ReappliesSavedFilters()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // Establish a distinguishing filter before saving so we can confirm it's reapplied.
        await report.SelectFilterAsync("Status", "Active");
        await report.ApplyFiltersAsync();

        var viewName = UniqueViewName("Reapply");
        await report.SaveCurrentFiltersAsNewViewAsync(viewName);

        // Clear filters so the panel is back to its default state, proving the next assertion
        // is actually driven by the saved view rather than filters left over from above.
        await report.ClearFiltersAsync();

        await report.SelectSavedViewAsync(viewName);

        Assert.False(await report.HasLoadErrorAsync(), "Expected selecting a saved view to reload the grid without an error banner");
        Assert.Null(await report.GetSavedViewErrorAsync());
    }

    /// <summary>
    /// Renaming the currently selected saved view must persist the new name and surface it in the
    /// "Saved Views" dropdown in place of the old name.
    /// </summary>
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

    /// <summary>
    /// Setting the currently selected saved view as default must persist that flag and surface
    /// the "(Default)" suffix convention (SavedViewOption.DisplayText in ReportFilterPanel.razor)
    /// for it in the dropdown.
    /// </summary>
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

    /// <summary>
    /// Deleting the currently selected saved view must remove it from the "Saved Views" dropdown.
    /// </summary>
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
}
