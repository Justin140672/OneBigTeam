using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class KeyboardAuthJourneyTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string TomEmail = "tom.williams@acme.example";
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    [Fact]
    public async Task Login_CanBeCompletedWithKeyboardOnly()
    {
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/login");
        await _page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });

        var email = _page.GetByPlaceholder("you@example.com");
        await email.FocusAsync();
        Assert.True(await email.EvaluateAsync<bool>("el => el === document.activeElement"),
            "Expected the email field to be focusable.");
        await _page.Keyboard.TypeAsync(TomEmail);

        for (var i = 0; i < 6 && await FocusedTypeAsync() != "password"; i++)
            await _page.Keyboard.PressAsync("Tab");
        Assert.Equal("password", await FocusedTypeAsync());
        await _page.Keyboard.TypeAsync(DevPersonaPassword);

        var loginButton = _page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        for (var i = 0; i < 6 && !await loginButton.EvaluateAsync<bool>("el => el === document.activeElement"); i++)
            await _page.Keyboard.PressAsync("Tab");
        Assert.True(await loginButton.EvaluateAsync<bool>("el => el === document.activeElement"),
            "Expected keyboard focus to reach the Login button.");
        await _page.Keyboard.PressAsync("Enter");

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 45_000 });
    }

    private Task<string> FocusedTypeAsync() =>
        _page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('type') ?? document.activeElement?.type ?? ''");
}
