using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: keyboard-only authentication journey — Tab through the <c>/login</c> form fields in DOM
/// order, type credentials, and submit with the keyboard, reaching the app shell. Uses
/// <see cref="SupabaseAuthSerialBlankTestBase"/> because it drives a real (uncached) Supabase
/// password-grant login rather than the storageState fast path.
/// </summary>
public sealed class KeyboardAuthJourneyTests(ParallelBlankPersonaFixture fixture)
    : SupabaseAuthSerialBlankTestBase(fixture)
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

        // Tab forward to the password field — there is now a "Forgot password?" link between the
        // email and password inputs, so don't assume a fixed number of hops.
        for (var i = 0; i < 6 && await FocusedTypeAsync() != "password"; i++)
            await _page.Keyboard.PressAsync("Tab");
        Assert.Equal("password", await FocusedTypeAsync());
        await _page.Keyboard.TypeAsync(DevPersonaPassword);

        // Tab forward to the submit button — there is a Show/Hide-password toggle button in
        // between. Identify it the same way LoginPage.cs's own click helper does (accessible
        // role/name), not by a "type=submit" attribute: Syncfusion's SfButton does not
        // necessarily render that attribute, so sniffing for it is unreliable and previously
        // let the loop run past the button entirely into the page's legal-links nav.
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
