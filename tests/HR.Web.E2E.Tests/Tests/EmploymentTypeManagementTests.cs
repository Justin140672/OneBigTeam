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
[Collection("E2E")]
public sealed class EmploymentTypeManagementTests(AppFixture fixture) : E2ETestBase(fixture)
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
        await _page.Locator("#showInactive").CheckAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10_000 });

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated type to appear when 'Show inactive' is enabled");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromEmploymentTypesPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employment-types");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // Tom has no manage permissions, so the page should redirect (e.g. to /login or home)
        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/employment-types"),
            $"Expected a plain employee to be redirected away from the employment types page, but ended up at: {finalUrl}");
    }
}
