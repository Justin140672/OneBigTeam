using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LoginPage(IPage page, string baseUrl)
{
    public async Task GoToAsync() =>
        await page.GotoAsync($"{baseUrl}/login");

    public async Task LoginAsync(string email, string password = "password")
    {
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.GetByPlaceholder("••••••••").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();

        // Wait until the shell has loaded (sidebar is the first thing painted after auth).
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }

    public async Task SwitchAccountAsync(string email, string password = "password")
    {
        // "Switch account" link is in the sidebar footer.
        await page.Locator("a.sidebar-login-link").ClickAsync();
        await page.WaitForURLAsync($"{baseUrl}/login");
        await LoginAsync(email, password);
    }
}
