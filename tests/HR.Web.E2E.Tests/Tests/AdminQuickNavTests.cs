using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-07 — the permission-aware quick-navigation palette (Ctrl+K):
/// - HR admin can jump to Leave Policies and find Compliance Centre.
/// - A plain employee has no trigger and Ctrl+K is inert (no reachable admin destinations).
/// - Esc closes the palette and returns focus to the trigger.
/// </summary>
public sealed class AdminQuickNavTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task HrAdmin_QuickNav_NavigatesToLeavePolicies()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");

        await palette.OpenWithKeyboardAsync();
        await palette.WaitForOpenAsync();

        await palette.TypeAsync("leave");
        Assert.True(await palette.HasResultAsync("Leave Policies"),
            "Expected a 'Leave Policies' option in the quick-nav results for an HR admin");

        await palette.ActivateFirstResultAsync();

        await _page.WaitForURLAsync($"**/companies/{AcmeId}/leave-policies", new() { Timeout = 30_000 });
        Assert.Contains($"/companies/{AcmeId}/leave-policies", _page.Url);
    }

    [Fact]
    public async Task HrAdmin_QuickNav_SurfacesComplianceCentre()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");

        await palette.OpenWithKeyboardAsync();
        await palette.WaitForOpenAsync();

        await palette.TypeAsync("compliance");

        Assert.True(await palette.HasResultAsync("Compliance Centre"),
            "Expected a 'Compliance Centre' result when searching 'compliance' as an HR admin");
    }

    [Fact]
    public async Task PlainEmployee_HasNoQuickNavTrigger_AndCtrlKIsInert()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}");

        Assert.Equal(0, await palette.Trigger.CountAsync());

        await palette.OpenWithKeyboardAsync();
        await _page.WaitForTimeoutAsync(1_000);

        Assert.Equal(0, await palette.Dialog.CountAsync());
    }

    [Fact]
    public async Task Escape_ClosesPalette_AndReturnsFocusToTrigger()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");

        await palette.OpenWithKeyboardAsync();
        await palette.WaitForOpenAsync();

        await palette.PressEscapeAsync();

        await palette.Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await Assertions.Expect(palette.Trigger).ToBeFocusedAsync();
    }
}
