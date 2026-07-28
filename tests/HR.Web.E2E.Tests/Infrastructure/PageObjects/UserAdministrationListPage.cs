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
        await page.GetByPlaceholder("Search by name or email").FillAsync(nameOrEmailFragment);
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
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Invite Employee User" }).ClickAsync();
        await InviteDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator InviteDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee User" });

    /// <summary>
    /// Runs the full 3-step invite wizard: selects the employee by name (Step 1) — this
    /// auto-derives the email from the employee's own work email, which is shown read-only and
    /// can no longer be edited here — then selects the given role(s) (Step 2), then confirms
    /// (Step 3). The employee combobox is a Syncfusion SfDropDownList (single-select popup, click
    /// item to choose and auto-close); the roles field is an SfMultiSelect in checkbox mode
    /// (popup stays open after each click, so it's closed explicitly before advancing) — same
    /// interaction patterns as SharedDocumentAudienceTests / CompanyDocumentsTabTests use for the
    /// equivalent Syncfusion widgets elsewhere in this suite.
    /// </summary>
    public async Task InviteEmployeeAsync(string employeeName, IReadOnlyList<string> roleNames)
    {
        // Step 1: Employee picker (Syncfusion SfDropDownList, AllowFiltering="true").
        await InviteDialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        if (await filterInput.CountAsync() > 0)
            await filterInput.FillAsync(employeeName);

        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = employeeName })
            .First
            .ClickAsync();

        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 2: Roles (email is derived from the employee record and shown read-only).
        await InviteDialog.Locator("input[placeholder='Select one or more roles']").ClickAsync();
        await page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });
        foreach (var roleName in roleNames)
        {
            await page.Locator(".e-popup .e-list-item")
                .Filter(new() { HasText = roleName })
                .First
                .ClickAsync();
        }
        // Checkbox-mode multiselect popups stay open to allow further selections, so click a
        // neutral area of the dialog (the nav-pills step header) to close the popup via its
        // outside-click handler before reaching for the footer "Next" button.
        await InviteDialog.Locator("ul.nav-pills").ClickAsync();

        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 3: Confirm.
        await InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Send Invite" }).ClickAsync();

        // Successful submission navigates away to the new user's detail page.
        await page.WaitForURLAsync("**/user-administration/*", new() { Timeout = 20_000 });
    }
}
