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
        // In dev mode, persona switcher is a dropdown in the topbar.
        var devSelect = page.Locator("select.dev-persona-select");
        if (await devSelect.IsVisibleAsync())
        {
            // Find the option whose label contains the email, then fall back to full login.
            var options = await devSelect.Locator("option").AllAsync();
            foreach (var opt in options)
            {
                var val = await opt.GetAttributeAsync("value");
                if (string.IsNullOrEmpty(val)) continue;
                // We identify the persona by matching email to the login page instead.
            }
            // Fall through to cookie-based login when we don't have a userId mapping.
        }

        // Fallback: navigate to /login and re-authenticate.
        await page.GotoAsync($"{baseUrl}/login");
        await page.WaitForURLAsync($"{baseUrl}/login");
        await LoginAsync(email, password);
    }

    public async Task SwitchPersonaAsync(string userId)
    {
        // Dev-mode topbar persona switcher.
        var devSelect = page.Locator("select.dev-persona-select");
        await devSelect.SelectOptionAsync(new SelectOptionValue { Value = userId });
        // Selecting triggers a full navigation, wait for the shell to reload.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }
}
