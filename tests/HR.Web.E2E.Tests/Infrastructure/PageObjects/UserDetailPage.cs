using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserDetail.razor ("/companies/{CompanyId}/user-administration/{EmployeeId}")
/// and the ManageUserRolesDialog.razor it opens via "Manage Roles".
/// </summary>
public sealed class UserDetailPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".card, .alert-warning";

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/user-administration/{employeeId}");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public async Task<string> GetAccountStatusAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".d-flex.align-items-center.gap-2.mb-1 span.badge").First.InnerTextAsync();
    }

    public async Task<int> GetAuditHistoryCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        return await card.Locator("ul.list-unstyled > li").CountAsync();
    }

    public async Task<bool> HasAuditHistoryEmptyMessageAsync()
    {
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        return await card.GetByText("No audit history recorded for this user.").IsVisibleAsync();
    }

    public async Task ResendInvitationAsync() => await ClickActionAsync("Resend Invitation");

    public async Task CancelInvitationAsync() => await ClickActionAsync("Cancel Invitation");

    public async Task DisableAccountAsync() => await ClickActionAsync("Disable Account");

    public async Task EnableAccountAsync() => await ClickActionAsync("Enable Account");

    private async Task ClickActionAsync(string buttonName)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = buttonName });
        await button.ClickAsync();

        // UserDetail.razor renders no spinner during _actionInProgress (Disabled="@_actionInProgress"
        // is the only visual cue), so there is nothing to wait on there. Instead wait for the clicked
        // button itself to disappear — every one of these four actions (Disable/Enable
        // Account, Resend/Cancel Invitation) changes AccountStatus/InvitationStatus once the
        // server round-trip and reload complete, which always swaps out or removes the action bar
        // entirely, so the exact button just clicked reliably stops being visible.
        await button.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<string?> GetSuccessMessageAsync()
    {
        var alert = page.Locator(".alert-success");
        try
        {
            await alert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }

        return await alert.InnerTextAsync();
    }

    public async Task OpenManageRolesDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Manage Roles" }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ManageRolesDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Manage Roles" });

    /// <summary>
    /// Toggles the given roles (by name) via the dialog's plain checkbox table and saves. Toggling
    /// a role that's already selected deselects it, and vice versa — callers should pass only the
    /// roles they want to flip relative to the dialog's pre-populated <c>CurrentRoleIds</c>.
    /// </summary>
    public async Task ToggleRolesAndSaveAsync(IReadOnlyList<string> roleNamesToToggle)
    {
        foreach (var roleName in roleNamesToToggle)
        {
            await ManageRolesDialog.Locator("tr", new() { HasText = roleName })
                .Locator("input[type='checkbox']")
                .First
                .ClickAsync();
        }

        await ManageRolesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Account Details" });
        var badges = card.Locator(".badge.bg-secondary-subtle");
        var count = await badges.CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
            names.Add(await badges.Nth(i).InnerTextAsync());
        return names;
    }

    // ── Permission Overrides (UserDetail.razor's "Permission Overrides" card) ──────────────────

    /// <summary>
    /// The "Permission Override" badge shown next to the Account/Invitation Status badges when
    /// the user has at least one active override — scoped to the header row (not the
    /// Permission Overrides card body) via ".d-flex.align-items-center.gap-2.mb-1", the same
    /// container <see cref="GetAccountStatusAsync"/> reads from.
    /// </summary>
    public Task<bool> HasPermissionOverrideBadgeAsync() =>
        page.Locator(".d-flex.align-items-center.gap-2.mb-1 span.badge")
            .Filter(new() { HasText = "Permission Override" })
            .IsVisibleAsync();

    private ILocator PermissionOverridesCard => page.Locator(".card", new() { HasText = "Permission Overrides" });

    public async Task<bool> HasNoOverridesMessageAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        return await PermissionOverridesCard.GetByText("No permission overrides for this user.").IsVisibleAsync();
    }

    /// <summary>Number of override rows currently listed in the "Permission Overrides" card.</summary>
    public async Task<int> GetOverrideCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        return await PermissionOverridesCard.Locator("ul.list-unstyled > li").CountAsync();
    }

    /// <summary>The override row (li) whose role name contains <paramref name="roleName"/>.</summary>
    private ILocator OverrideRow(string roleName) =>
        PermissionOverridesCard.Locator("li").Filter(new() { HasText = roleName });

    public Task<bool> HasOverrideAsync(string roleName) => OverrideRow(roleName).IsVisibleAsync();

    public async Task<string?> GetOverrideTypeAsync(string roleName) =>
        (await OverrideRow(roleName).Locator(".badge").InnerTextAsync())?.Trim();

    public async Task OpenAddOverrideDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Add Override" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Add Permission Override" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the per-row "Remove" button for the override on <paramref name="roleName"/> and
    /// waits for the row to disappear (UserDetail.razor's RemoveOverrideAsync reloads the page's
    /// state on success).
    /// </summary>
    public async Task RemoveOverrideAsync(string roleName)
    {
        var row = OverrideRow(roleName);
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await row.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Effective Access (UserDetail.razor's "Effective Access" card, IAM-05) ───────────────────

    private ILocator EffectiveAccessCard => page.Locator(".card", new() { HasText = "Effective Access" });

    /// <summary>
    /// Waits for the Effective Access card to finish loading (i.e. the async
    /// GetEffectiveAccessAsync call has resolved and either the data or an error is rendered).
    /// </summary>
    public async Task WaitForEffectiveAccessLoadedAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        await EffectiveAccessCard.Locator("h6", new() { HasText = "Position Profile" })
            .Or(EffectiveAccessCard.Locator(".alert-warning"))
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> HasEffectiveAccessErrorAsync() =>
        EffectiveAccessCard.Locator(".alert-warning").IsVisibleAsync();

    private ILocator SectionOf(string heading) =>
        EffectiveAccessCard.Locator("h6", new() { HasText = heading }).Locator("xpath=..");

    public async Task<string> GetPositionProfileTextAsync() =>
        (await SectionOf("Position Profile").InnerTextAsync()).Replace("Position Profile", "").Trim();

    public async Task<IReadOnlyList<string>> GetDirectRoleNamesAsync() =>
        await SectionOf("Direct Roles").Locator(".badge").AllInnerTextsAsync();

    public async Task<IReadOnlyList<string>> GetInheritedRoleNamesAsync() =>
        await SectionOf("Inherited Roles").Locator(".badge").AllInnerTextsAsync();

    public async Task<IReadOnlyList<string>> GetEffectiveAccessOverrideRoleNamesAsync()
    {
        var section = SectionOf("Overrides");
        var count = await section.Locator("div.mb-1").CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
            names.Add((await section.Locator("div.mb-1").Nth(i).Locator("span.fw-semibold").InnerTextAsync()).Trim());
        return names;
    }

    /// <summary>The Effective Roles list item (li) whose role name contains <paramref name="roleName"/>.</summary>
    private ILocator EffectiveRoleRow(string roleName) =>
        SectionOf("Effective Roles").Locator("li").Filter(new() { HasText = roleName });

    public Task<bool> HasEffectiveRoleAsync(string roleName) => EffectiveRoleRow(roleName).IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetEffectiveRoleSourcesAsync(string roleName) =>
        await EffectiveRoleRow(roleName).Locator(".badge").AllInnerTextsAsync();

    public async Task<bool> HasEffectivePermissionAsync(string permissionName) =>
        await SectionOf("Effective Permissions").Locator("li").Filter(new() { HasText = permissionName }).IsVisibleAsync();

    public async Task<int> GetDeniedPermissionsCountAsync() =>
        await SectionOf("Denied Permissions").Locator("li").CountAsync();

    public async Task<bool> HasDeniedPermissionAsync(string permissionName) =>
        await SectionOf("Denied Permissions").Locator("li").Filter(new() { HasText = permissionName }).IsVisibleAsync();
}
