using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserAdministrationList.razor (the "/companies/{CompanyId}/user-administration"
/// grid). Inviting an employee is done from the Employee List's row-level "Invite User" action
/// (see EmployeeListPage) — this page no longer has its own invite entry point.
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
            .WaitUntilVisibleAsync();
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

    // ── Resend/Cancel Invitation toolbar actions ──────────────────────────────
    // UserAdministrationList.razor (SearchPageBase<UserListItemModel>) — selecting a row (clicking
    // it, same as DepartmentListPage.DeactivateDepartmentAsync) enables the "Resend Invitation"/
    // "Cancel Invitation" toolbar buttons; the handlers themselves no-op with an inline error
    // (surfaced in ".alert-danger") if the selected row isn't an actionable pending/expired invite.

    /// <summary>Clicks the row containing <paramref name="nameOrEmailFragment"/> to select it for a toolbar action.</summary>
    public async Task SelectRowAsync(string nameOrEmailFragment)
    {
        await SearchAsync(nameOrEmailFragment);

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First;
        await row.ClickAsync();
    }

    public async Task ClickResendInvitationAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Resend Invitation" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
    }

    public async Task ClickCancelInvitationAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Cancel Invitation" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
    }

    /// <summary>Returns the inline action-error alert text, or null if it isn't showing.</summary>
    public async Task<string?> GetActionErrorAsync()
    {
        var alert = page.Locator(".alert-danger.alert-dismissible");
        if (!await alert.IsVisibleAsync())
            return null;
        return (await alert.InnerTextAsync()).Trim();
    }
}
