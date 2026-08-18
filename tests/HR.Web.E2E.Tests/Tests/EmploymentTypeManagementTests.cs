using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator CRUD workflows for employment types:
/// - Create a new employment type and verify it appears in the list.
/// - Deactivate an employment type.
/// - A plain Employee is redirected away from the employment types page (no manage access).
/// </summary>
public sealed class EmploymentTypeManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task CreateEmploymentType_AppearsInList()
    {
        var typeName = $"E2E Type {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new EmploymentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new EmploymentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();

        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillDescriptionAsync("Created by E2E test");
        await typeEdit.SaveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            $"Expected the new employment type '{typeName}' to appear in the list after creation");
    }

    [Fact]
    public async Task DeactivateEmploymentType_ShowsInactiveBadge()
    {
        var typeName = $"E2E Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new EmploymentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new EmploymentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create first
        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.SaveAsync();

        // Now deactivate
        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.IsActiveAsync(typeName), "Expected newly created type to be Active");
        await typeList.DeactivateAsync(typeName);

        // Show inactive and verify
        await typeList.ShowInactiveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated type to appear when 'Show inactive' is enabled");
    }

    [Fact]
    public async Task EditEmploymentType_PersistsAcrossReload()
    {
        var originalName = $"E2E Type Edit {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName  = $"{originalName} Updated";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new EmploymentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new EmploymentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(originalName);
        await typeEdit.FillDescriptionAsync("Created by E2E test");
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = originalName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await typeEdit.FillNameAsync(updatedName);
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        var updatedHref = await _page.Locator(".e-rowcell a").Filter(new() { HasText = updatedName }).First.GetAttributeAsync("href");
        Assert.NotNull(updatedHref);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{updatedHref}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        // Reload the page directly to confirm the change persisted server-side, not just in local state.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        Assert.Equal(updatedName, await typeEdit.GetNameAsync());
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromEmploymentTypesPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employment-types");
        // See WaitForUrlToStopContainingAsync's doc comment: the redirect is a client-side Blazor
        // NavigateTo, not a full navigation, so NetworkIdle is not a reliable completion signal.
        await WaitForUrlToStopContainingAsync("/employment-types");

        // Tom has no manage permissions, so the page should redirect (e.g. to /login or home)
        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/employment-types"),
            $"Expected a plain employee to be redirected away from the employment types page, but ended up at: {finalUrl}");
    }
}
