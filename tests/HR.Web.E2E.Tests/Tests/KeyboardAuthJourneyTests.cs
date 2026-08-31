using HR.Web.E2E.Tests.Infrastructure;

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

        await _page.Keyboard.PressAsync("Tab");
        await _page.Keyboard.TypeAsync(DevPersonaPassword);

        // Tab to the submit button and activate it from the keyboard.
        await _page.Keyboard.PressAsync("Tab");
        var focusedTag = await _page.EvaluateAsync<string>("() => document.activeElement?.tagName ?? ''");
        Assert.Equal("BUTTON", focusedTag);
        await _page.Keyboard.PressAsync("Enter");

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 45_000 });
    }
}
