using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LoginPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/login");
        // Wait for the Blazor circuit to render the interactive form before LoginAsync tries to fill it.
        await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
    }

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
        // In dev mode, persona switcher is an SfDropDownList in the topbar — we don't have a
        // userId mapping from an email here, so this always falls back to cookie-based login.
        await page.GotoAsync($"{baseUrl}/login");
        await page.WaitForURLAsync($"{baseUrl}/login");
        await LoginAsync(email, password);
    }

    public async Task SwitchPersonaAsync(string personaNameFragment)
    {
        // Dev-mode topbar persona switcher — Syncfusion SfDropDownList, see DropDownSelector.
        await DropDownSelector.SelectAsync(page, page.Locator(".dev-persona-switcher"), personaNameFragment);
        // Selecting triggers a full navigation, wait for the shell to reload.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }
}
