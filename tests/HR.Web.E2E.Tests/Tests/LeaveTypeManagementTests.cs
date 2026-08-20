using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LeaveTypeManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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
        // Default Days is only editable for a type literally named "Annual Leave" (item 50 —
        // see LeaveTypeEdit.razor's IsAnnualLeave); this type isn't, so the field renders as
        // read-only text and there's nothing to fill.
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
        // See CreateLeaveType_AppearsInList — Default Days only applies to the "Annual Leave" type.
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.IsActiveAsync(typeName), "Expected newly created type to be Active");
        await typeList.DeactivateAsync(typeName);

        await typeList.ShowInactiveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated type to appear when 'Show inactive' is enabled");
    }

    [Fact]
    public async Task EditLeaveType_PersistsAcrossReload()
    {
        var suffix       = Guid.NewGuid().ToString("N")[..8];
        var originalName = $"E2E Leave Edit {suffix}";
        var updatedName  = $"{originalName} Updated";
        var typeCode     = $"E2E{suffix[..4].ToUpperInvariant()}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(originalName);
        await typeEdit.FillCodeAsync(typeCode);
        // See CreateLeaveType_AppearsInList — Default Days only applies to the "Annual Leave" type.
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
