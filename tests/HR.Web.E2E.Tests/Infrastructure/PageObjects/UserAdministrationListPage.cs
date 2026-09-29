using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class UserAdministrationListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger, .alert-info";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/user-administration");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    private async Task SearchAsync(string nameOrEmailFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var searchInput = page.GetByPlaceholder("Search by name or email");
        await searchInput.FillAsync(nameOrEmailFragment);
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

    public async Task<string?> GetInvitationStatusAsync(string nameOrEmailFragment, string? expectedStatus = null)
    {
        await SearchAsync(nameOrEmailFragment);

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First;
        var badge = row.Locator(".e-rowcell .badge").Last;

        string? current = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await badge.IsVisibleAsync())
            {
                current = (await badge.InnerTextAsync()).Trim();
                if (expectedStatus is null || string.Equals(current, expectedStatus, StringComparison.Ordinal))
                    return current;
            }

            await page.WaitForTimeoutAsync(250);
        }

        return current;
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

    public async Task<string?> GetActionErrorAsync()
    {
        var alert = page.Locator(".alert-danger.alert-dismissible");
        try
        {
            await alert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await alert.InnerTextAsync()).Trim();
    }
}
