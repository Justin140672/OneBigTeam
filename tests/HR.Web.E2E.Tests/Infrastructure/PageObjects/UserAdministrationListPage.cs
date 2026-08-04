using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserAdministrationList.razor (the "/companies/{CompanyId}/user-administration"
/// grid) and the InviteUserDialog.razor wizard it opens.
/// </summary>
public sealed class UserAdministrationListPage(IPage page, string baseUrl)
{
    // ".e-grid" alone doesn't prove rows are queryable — Syncfusion's EJ2 grid populates
    // ".e-row"/".e-rowcell" on its own JS render tick after the Blazor component mounts, so the
    // row selector (or its empty-state/error siblings) is the only wait actually tied to data
    // being present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger, .alert-info";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/user-administration");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    // UserAdministrationList.razor (SearchPageBase<UserListItemModel>) fetches an unfiltered page
    // capped at 200 rows sorted by CreatedAt — on this shared, long-lived E2E database that cap is
    // easy to exceed, so a specific user (e.g. one just invited by another test) can silently fall
    // outside it. The page's own search box round-trips to the server (SearchPageBase.OnSearchChanged,
    // debounced ~300ms) and finds the user regardless of how many others sort before them — same
    // pattern as EmployeeListPage.HasEmployeeAsync.
    private async Task SearchAsync(string nameOrEmailFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var searchInput = page.GetByPlaceholder("Search by name or email");
        await searchInput.FillAsync(nameOrEmailFragment);
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change, not on the "input" event
        // Playwright's FillAsync dispatches — without an explicit Enter/blur here, the search
        // round-trip never actually fires (same reasoning as EmployeeListPage.HasEmployeeAsync).
        await searchInput.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<bool> HasRowAsync(string nameOrEmailFragment)
    {
        await SearchAsync(nameOrEmailFragment);

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task<string?> GetInvitationStatusAsync(string nameOrEmailFragment)
    {
        await SearchAsync(nameOrEmailFragment);

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First;

        // Invitation Status is the 4th data column (Name, Email, Roles, Account Status, Invitation Status).
        var badge = row.Locator(".e-rowcell .badge").Last;
        return await badge.IsVisibleAsync() ? await badge.InnerTextAsync() : null;
    }

    public async Task OpenUserDetailAsync(string nameOrEmailFragment)
    {
        await SearchAsync(nameOrEmailFragment);

        await page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First
            .ClickAsync();

        await page.WaitForURLAsync("**/user-administration/*", new() { Timeout = 15_000 });
    }

    public async Task OpenInviteDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Invite Employee" }).ClickAsync();
        await InviteDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// Dialog header is "Invite Employee" (InviteUserDialog.razor) — not "Invite Employee User",
    /// which is a distinct, older label that used to render here.
    /// </summary>
    public ILocator InviteDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee" });

    /// <summary>
    /// Runs the full 3-step invite wizard from this entry point (no pre-selected employee, so
    /// step 1 — "Employee" — is shown): selects the employee by name (Step 1) — this auto-derives
    /// the email from the employee's own work email — then checks any additional (non-Employee)
    /// role(s) in the plain checkbox table (Step 2; the mandatory "Employee" role itself is shown
    /// as a fixed, non-removable badge above the table and cannot be selected/deselected —
    /// see <see cref="IsEmployeeRoleBadgeVisibleAsync"/>), then confirms (Step 3, which shows no
    /// separate Email row — see <see cref="HasConfirmEmailRowAsync"/>). The employee combobox is a
    /// Syncfusion SfDropDownList (single-select popup, click item to choose and auto-close); the
    /// roles field is a plain HTML checkbox table (no popup), same pattern as
    /// UserDetailPage.ToggleRolesAndSaveAsync uses for ManageUserRolesDialog.
    /// </summary>
    public async Task InviteEmployeeAsync(string employeeName, IReadOnlyList<string> additionalRoleNames)
    {
        // Step 1: Employee picker (Syncfusion SfDropDownList, AllowFiltering="true").
        // DropDownSelector itself confirms Blazor's ValueChanged round-trip (InviteUserDialog's
        // own OnEmployeeChanged, which populates _email from the selected employee) actually
        // committed before returning — see its own doc comment. Without that, clicking "Next"
        // can race the round-trip: _selectedEmployeeId is already set (step 1's own check
        // passes) but _email hasn't been populated yet, surfacing a bogus "no work email on
        // file" error on step 2 for an employee who genuinely has one.
        await DropDownSelector.SelectAsync(page, InviteDialog, employeeName);

        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // DropDownSelector only proves the combobox's own displayed text updated client-side —
        // not that the SignalR round trip invoking InviteUserDialog's OnEmployeeChanged (which
        // populates _email) has landed server-side yet (see DropDownSelector's own doc comment).
        // If "Next" above raced ahead of that commit, the dialog surfaces a spurious "no work
        // email on file" step error even for an employee who genuinely has one. Self-heal: step
        // back and retry — by then the round trip will have committed.
        var noEmailError = InviteDialog.GetByText("This employee has no work email on file", new() { Exact = false });
        if (await noEmailError.IsVisibleAsync())
        {
            await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
            await page.WaitForTimeoutAsync(500);
            await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        }

        // Step 2: additional roles only — "Employee" is a fixed badge, never a selectable item
        // (see InviteUserDialog.razor's _additionalRoleOptions, which excludes it entirely). Plain
        // checkbox table, same pattern as UserDetailPage.ToggleRolesAndSaveAsync for ManageRolesDialog.
        foreach (var roleName in additionalRoleNames)
        {
            await InviteDialog.Locator("tr", new() { HasText = roleName })
                .Locator("input[type='checkbox']")
                .First
                .ClickAsync();
        }

        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 3: Confirm.
        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Send Invite" }).ClickAsync();

        // Successful submission navigates away to the new user's detail page.
        await page.WaitForURLAsync("**/user-administration/*", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// True if the (currently open, step 2) dialog shows the mandatory "Employee" role as a fixed
    /// badge rather than a removable multiselect item.
    /// </summary>
    public Task<bool> IsEmployeeRoleBadgeVisibleAsync() =>
        InviteDialog.Locator("span.badge", new() { HasText = "Employee" }).IsVisibleAsync();

    /// <summary>
    /// Returns whether the "Employee" step pill (VisibleSteps' "1. Employee") is shown in the
    /// wizard's step nav — false when the dialog was launched with a pre-selected employee (see
    /// InviteUserDialog.razor's VisibleSteps).
    /// </summary>
    public Task<bool> HasEmployeeStepPillAsync() =>
        InviteDialog.GetByText("Employee", new() { Exact = false })
            .Locator("xpath=ancestor::li[contains(@class,'nav-item')]")
            .First
            .IsVisibleAsync();

    /// <summary>
    /// True if the (currently open, step 3 Confirm) dialog shows a separate "Email" row in its
    /// summary — expected false, since InviteUserDialog.razor's step 3 summary only ever shows
    /// Employee/Roles rows, not Email.
    /// </summary>
    public Task<bool> HasConfirmEmailRowAsync() =>
        InviteDialog.Locator("dt", new() { HasText = "Email" }).IsVisibleAsync();
}
