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
        await page.GetByRole(AriaRole.Button, new() { Name = buttonName }).ClickAsync();
        // The action buttons re-render the whole action bar once the request completes and the
        // page reloads its detail data — wait for the request-in-flight spinner (if any) to clear.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<string?> GetSuccessMessageAsync()
    {
        var alert = page.Locator(".alert-success");
        return await alert.IsVisibleAsync() ? await alert.InnerTextAsync() : null;
    }

    public async Task OpenManageRolesDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Manage Roles" }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ManageRolesDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Manage Roles" });

    /// <summary>
    /// Toggles the given roles (by name) in the checkbox-mode SfMultiSelect and saves. Toggling a
    /// role that's already selected deselects it, and vice versa — callers should pass only the
    /// roles they want to flip relative to the dialog's pre-populated <c>CurrentRoleIds</c>.
    /// </summary>
    public async Task ToggleRolesAndSaveAsync(IReadOnlyList<string> roleNamesToToggle)
    {
        await ManageRolesDialog.Locator("input[placeholder='Select one or more roles']").ClickAsync();
        await page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });

        foreach (var roleName in roleNamesToToggle)
        {
            await page.Locator(".e-popup .e-list-item")
                .Filter(new() { HasText = roleName })
                .First
                .ClickAsync();
        }

        // Checkbox-mode multiselect popups stay open to allow further selections, so click a
        // neutral area of the dialog (its instructional paragraph) to close the popup via its
        // outside-click handler before reaching for the footer Save button.
        await ManageRolesDialog.Locator("p.text-muted.small").ClickAsync();

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
}
