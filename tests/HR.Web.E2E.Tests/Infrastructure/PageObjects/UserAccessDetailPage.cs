using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserAccessDetail.razor
/// ("/companies/{CompanyId}/user-administration/{EmployeeId}/access-details"), the secondary page
/// hosting the "Permission Overrides", "Effective Access" (IAM-05), and "Audit History" cards that
/// used to live on UserDetail.razor, plus the AddRoleOverrideDialog.razor it opens via
/// "+ Add Override".
/// </summary>
public sealed class UserAccessDetailPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".card, .alert-warning";

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/user-administration/{employeeId}/access-details");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
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

    public async Task BackToUserDetailAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
    }

    // ── Audit History ────────────────────────────────────────────────────────────────────────

    public async Task<int> GetAuditHistoryCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        return await card.Locator("ul.list-unstyled > li").CountAsync();
    }

    public async Task<bool> HasAuditHistoryEmptyMessageAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        var emptyMessage = card.GetByText("No audit history recorded for this user.");
        var rows = card.Locator("ul.list-unstyled > li");

        // The Audit History card loads its data asynchronously after LoadedSelector appears,
        // so wait for whichever end-state (empty message or at least one row) renders first
        // before checking, rather than racing the initial fetch.
        try
        {
            await Task.WhenAny(
                emptyMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 }),
                rows.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 }));
        }
        catch (TimeoutException)
        {
            // Neither state appeared in time; fall through and report whatever is actually visible.
        }

        return await emptyMessage.IsVisibleAsync();
    }

    // ── Permission Overrides ─────────────────────────────────────────────────────────────────

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
    /// waits for the row to disappear (UserAccessDetail.razor's RemoveOverrideAsync reloads the
    /// page's state on success).
    /// </summary>
    public async Task RemoveOverrideAsync(string roleName)
    {
        var row = OverrideRow(roleName);
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await row.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Effective Access (IAM-05) ────────────────────────────────────────────────────────────

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
