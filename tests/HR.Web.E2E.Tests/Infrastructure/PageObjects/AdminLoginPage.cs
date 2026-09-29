using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class AdminLoginPage(IPage page, string baseUrl)
{
    private const string DevPersonaLoginPassword = "password";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/login", new() { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
    }

    public async Task LoginAsync(string email, string password = DevPersonaLoginPassword)
    {
        await SubmitCredentialsAsync(email, password);

        await page.WaitForURLAsync(url => !url.ToString().Contains("/login"), new() { Timeout = 30_000 });
    }

    public async Task<string> SubmitExpectingNotAuthorisedAsync(string email, string password = DevPersonaLoginPassword)
    {
        await SubmitCredentialsAsync(email, password);

        var error = page.Locator(".login-error");
        await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        return (await error.TextContentAsync())?.Trim() ?? "";
    }

    public bool IsOnLoginPage() => page.Url.Contains("/login");

    private async Task SubmitCredentialsAsync(string email, string password)
    {
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByPlaceholder("password").FillAsync(password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
