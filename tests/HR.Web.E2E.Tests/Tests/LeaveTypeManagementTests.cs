using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

[Collection("E2E")]
public sealed class LeaveTypeManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task CreateLeaveType_AppearsInList()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var typeName = $"E2E Leave {suffix}";
        var typeCode = $"E2E{suffix[..4].ToUpperInvariant()}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();

        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillCodeAsync(typeCode);
        await typeEdit.FillDefaultDaysAsync(10);
        await typeEdit.SaveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            $"Expected the new leave type '{typeName}' to appear in the list after creation");
    }

    [Fact]
    public async Task DeactivateLeaveType_ShowsInactiveBadge()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var typeName = $"E2E Deact {suffix}";
        var typeCode = $"DC{suffix[..6].ToUpperInvariant()}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillCodeAsync(typeCode);
        await typeEdit.FillDefaultDaysAsync(5);
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.IsActiveAsync(typeName), "Expected newly created type to be Active");
        await typeList.DeactivateAsync(typeName);

        await typeList.ShowInactiveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated type to appear when 'Show inactive' is enabled");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromLeaveTypesPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/leave-types");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/leave-types"),
            $"Expected a plain employee to be redirected away from the leave types page, but ended up at: {finalUrl}");
    }
}
