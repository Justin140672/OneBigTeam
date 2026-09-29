using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DialogFocusManagementTests(CrossUserFixture fixture)
    : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task LoginAsync(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);
    }

    [Fact]
    public async Task RequestLeaveDialog_TrapsFocus_AndRestoresToTrigger()
    {
        await LoginAsync(TomEmail);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        var trigger = _page.GetByRole(AriaRole.Button, new() { Name = "Request Leave" });
        var dialog  = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        await DialogAccessibility.AssertFocusRestoredAsync(
            _page,
            openDialog: async () =>
            {
                await trigger.ClickAsync();
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await DialogAccessibility.AssertFocusTrappedAsync(_page, dialog);
            },
            closeDialog: async () =>
            {
                await _page.Keyboard.PressAsync("Escape");
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            },
            triggerButton: trigger);
    }

    [Fact]
    public async Task HrConfirmDialog_TrapsFocus_AndRestoresToTrigger()
    {
        await LoginAsync(LauraEmail);
        var leaveTypes = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        await leaveTypes.GoToAsync(AcmeId);
        await _page.Locator(".e-grid .e-row").Last.ClickAsync();

        var trigger = _page.Locator(".e-toolbar-item")
            .GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await trigger.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var dialog = _page.GetByRole(AriaRole.Dialog);

        await DialogAccessibility.AssertFocusRestoredAsync(
            _page,
            openDialog: async () =>
            {
                await trigger.ClickAsync();
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await DialogAccessibility.AssertFocusTrappedAsync(_page, dialog);
            },
            closeDialog: async () =>
            {
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            },
            triggerButton: trigger);
    }

    [Fact]
    public async Task ChangeProfilePhotoDialog_TrapsFocus_AndRestoresToTrigger()
    {
        await LoginAsync(TomEmail);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);

        var trigger = _page.GetByRole(AriaRole.Button, new() { Name = "Change Photo" });
        var dialog  = _page.GetByRole(AriaRole.Dialog, new() { Name = "Change Profile Photo" });

        await DialogAccessibility.AssertFocusRestoredAsync(
            _page,
            openDialog: async () =>
            {
                await trigger.ClickAsync();
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await DialogAccessibility.AssertFocusTrappedAsync(_page, dialog);
            },
            closeDialog: async () =>
            {
                await _page.Keyboard.PressAsync("Escape");
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            },
            triggerButton: trigger);
    }
}
