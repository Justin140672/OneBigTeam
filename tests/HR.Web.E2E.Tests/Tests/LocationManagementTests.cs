using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator CRUD workflows for locations:
/// - A new location can be created and appears in the list.
/// - A location can be edited and the change persists across a reload.
/// Uses the seeded "Office" location type on the Acme company (see EmployeesModule dev seed).
/// </summary>
[Collection("E2E")]
public sealed class LocationManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string SeededLocationTypeName = "Office";

    [Fact]
    public async Task CreateLocation_AppearsInList()
    {
        var locationName = $"E2E Location {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var locationList = new LocationListPage(_page, _fixture.WebBaseUrl);
        var locationEdit = new LocationEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await locationList.GoToAsync(AcmeId);
        await locationList.ClickNewLocationAsync();

        await locationEdit.FillNameAsync(locationName);
        await locationEdit.FillDescriptionAsync("Created by E2E test");
        await locationEdit.SelectLocationTypeAsync(SeededLocationTypeName);
        await locationEdit.SaveAsync();

        Assert.True(await locationList.HasLocationAsync(locationName),
            $"Expected the new location '{locationName}' to appear in the list after creation");
    }

    [Fact]
    public async Task EditLocation_PersistsAcrossReload()
    {
        var originalName = $"E2E Location Edit {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName = $"{originalName} Updated";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var locationList = new LocationListPage(_page, _fixture.WebBaseUrl);
        var locationEdit = new LocationEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await locationList.GoToAsync(AcmeId);
        await locationList.ClickNewLocationAsync();
        await locationEdit.FillNameAsync(originalName);
        await locationEdit.FillDescriptionAsync("Created by E2E test");
        await locationEdit.SelectLocationTypeAsync(SeededLocationTypeName);
        await locationEdit.SaveAsync();

        await locationList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = originalName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        await locationEdit.FillNameAsync(updatedName);
        await locationEdit.SaveAsync();

        await locationList.GoToAsync(AcmeId);
        var updatedHref = await _page.Locator(".e-rowcell a").Filter(new() { HasText = updatedName }).First.GetAttributeAsync("href");
        Assert.NotNull(updatedHref);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{updatedHref}");
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        // Reload the page directly to confirm the change persisted server-side, not just in local state.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        Assert.Equal(updatedName, await locationEdit.GetNameAsync());
    }
}
