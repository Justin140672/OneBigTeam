using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Leave Policy list/edit pages: loading, creating, editing, and the two ways to
/// mark a policy as the company's default (the "Is Default" checkbox on the edit form, and the
/// "Set as Default" toolbar action on the list).
/// </summary>
[Collection("E2E")]
public sealed class LeavePolicyManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task LeavePoliciesPage_Loads_WithSeededDefaultPolicy()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList  = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polList.GoToAsync(AcmeId);

        Assert.True(await polList.HasItemAsync("Standard"),
            "Expected the seeded 'Standard' leave policy to appear in the list");
    }

    [Fact]
    public async Task CreatePolicy_AppearsInList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"E2E Policy {suffix}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();

        await polEdit.FillNameAsync(name);
        await polEdit.SaveAsync();

        Assert.True(await polList.HasItemAsync(name),
            $"Expected the new leave policy '{name}' to appear in the list after creation");
    }

    [Fact]
    public async Task EditPolicy_PersistsAcrossReload()
    {
        var suffix       = Guid.NewGuid().ToString("N")[..8];
        var originalName = $"E2E Policy Edit {suffix}";
        var updatedName  = $"{originalName} Updated";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();
        await polEdit.FillNameAsync(originalName);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = originalName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await polEdit.FillNameAsync(updatedName);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        var updatedHref = await _page.Locator(".e-rowcell a").Filter(new() { HasText = updatedName }).First.GetAttributeAsync("href");
        Assert.NotNull(updatedHref);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{updatedHref}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        // Reload directly to confirm the change persisted server-side, not just in local state.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        Assert.Equal(updatedName, await polEdit.GetNameAsync());
    }

    [Fact]
    public async Task CreatePolicy_WithIsDefaultChecked_ShowsDefaultBadgeInList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"E2E Default {suffix}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();

        await polEdit.FillNameAsync(name);
        await polEdit.SetIsDefaultAsync(true);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        Assert.True(await polList.IsDefaultAsync(name),
            $"Expected '{name}' to show the Default badge after being created with Is Default checked");
    }

    [Fact]
    public async Task SetAsDefaultToolbarAction_SwapsDefaultBadgeToSelectedPolicy()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var first  = $"E2E ToolbarDefault A {suffix}";
        var second = $"E2E ToolbarDefault B {suffix}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create two new, non-default policies for this run.
        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();
        await polEdit.FillNameAsync(first);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();
        await polEdit.FillNameAsync(second);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        Assert.False(await polList.IsDefaultAsync(second),
            $"Expected '{second}' to not be the default immediately after creation");

        await polList.SetAsDefaultAsync(second);

        await polList.GoToAsync(AcmeId);
        Assert.True(await polList.IsDefaultAsync(second),
            $"Expected '{second}' to show the Default badge after using the 'Set as Default' toolbar action");
    }
}
