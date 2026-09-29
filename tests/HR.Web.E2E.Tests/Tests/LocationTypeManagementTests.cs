using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LocationTypeManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CreateLocationType_AppearsInList()
    {
        var typeName = $"E2E Location Type {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LocationTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LocationTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();

        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillDescriptionAsync("Created by E2E test");
        await typeEdit.SaveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            $"Expected the new location type '{typeName}' to appear in the list after creation");
    }

    [Fact]
    public async Task DeactivateLocationType_HiddenFromActiveList_VisibleWhenShowingInactive()
    {
        var typeName = $"E2E Location Type Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LocationTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LocationTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.IsActiveAsync(typeName), "Expected newly created location type to be Active");
        await typeList.DeactivateAsync(typeName);

        Assert.False(await typeList.HasItemAsync(typeName),
            "Expected deactivated location type to be hidden from the default active-only list");

        await typeList.ShowInactiveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated location type to appear when 'Show inactive' is enabled");
    }
}
