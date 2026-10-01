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
        // Syncfusion re-parents the inputs when the Blazor circuit attaches, which drops focus and any
        // keystrokes typed before that moment — there is no browser-visible "interactive" signal to
        // wait on, so a pass that loses focus or typed text is discarded and redone on a fresh load.
        string? lastFailure = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            lastFailure = await TryKeyboardLoginAsync();
            if (lastFailure is null) return;
        }

        Assert.Fail(lastFailure);
    }

    private async Task<string?> TryKeyboardLoginAsync()
    {
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/login");
        await _page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var email = _page.GetByPlaceholder("you@example.com");
        await email.FocusAsync();
        if (!await email.EvaluateAsync<bool>("el => el === document.activeElement"))
            return "Expected the email field to be focusable.";
        await _page.Keyboard.TypeAsync(TomEmail);

        for (var i = 0; i < 6 && await FocusedTypeAsync() != "password"; i++)
            await _page.Keyboard.PressAsync("Tab");
        if (await FocusedTypeAsync() != "password")
            return "Expected keyboard Tab order to reach the password field.";
        await _page.Keyboard.TypeAsync(DevPersonaPassword);

        var loginButton = _page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        for (var i = 0; i < 6 && !await loginButton.EvaluateAsync<bool>("el => el === document.activeElement"); i++)
            await _page.Keyboard.PressAsync("Tab");
        if (!await loginButton.EvaluateAsync<bool>("el => el === document.activeElement"))
            return "Expected keyboard focus to reach the Login button.";
        await _page.Keyboard.PressAsync("Enter");

        try
        {
            await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 20_000 });
            return null;
        }
        catch (TimeoutException)
        {
            return "Pressing Enter on the Login button did not reach the app shell.";
        }
    }

    private Task<string> FocusedTypeAsync() =>
        _page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('type') ?? document.activeElement?.type ?? ''");
}
