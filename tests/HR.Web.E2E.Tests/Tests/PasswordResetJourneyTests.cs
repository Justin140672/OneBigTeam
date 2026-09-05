using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the forgot-password / reset-password journey as far as this environment allows. There is
/// no live Supabase project configured for E2E, so a *successful* end-to-end recovery (a real
/// Supabase-issued recovery token in the URL fragment that HR.Api's POST /api/reset-password
/// accepts) cannot be driven here. This file therefore covers everything reachable without a live
/// Supabase project:
///   1. /forgot-password renders, accepts an email, and shows the deliberately non-enumerating
///      confirmation ("If an account exists for that email address, we've sent password reset
///      instructions.") — identical whether or not the address matches an account.
///   2. Direct navigation to /reset-password (no recovery token in the fragment) lands on the
///      "link no longer valid" panel with a route back to Forgot Password.
///
/// A third scenario — the reset form's client-side "passwords do not match" guard — was removed.
/// It navigated to /reset-password-complete with a fabricated, never-issued handoff code, but
/// ResetPasswordComplete.razor only renders the password form when AuthHandoffStore.Redeem(code)
/// resolves a session that was actually Issue()'d by a real Supabase recovery round trip
/// (AuthHandoffStore.cs) — a fabricated code always redeems to null and always lands on the "link
/// no longer valid" panel instead, making that scenario architecturally unreachable here rather
/// than flaky. Covering the mismatch guard would need either a live Supabase project or a
/// deliberate, environment-gated test seam into the handoff store, neither of which exists today.
/// </summary>
public sealed class PasswordResetJourneyTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    [Fact]
    public async Task ForgotPassword_ShowsNonEnumeratingConfirmation_ForUnknownEmail()
    {
        var page = new ForgotPasswordPage(_page, _fixture.WebBaseUrl);
        await page.GoToAsync();

        await page.SubmitAsync($"e2e-unknown-{Guid.NewGuid():N}@example.com");

        Assert.Equal(
            "If an account exists for that email address, we've sent password reset instructions.",
            await page.GetConfirmationTextAsync());
    }

    [Fact]
    public async Task ResetPassword_WithNoRecoveryToken_ShowsLinkNoLongerValid()
    {
        var page = new ResetPasswordPage(_page, _fixture.WebBaseUrl);
        await page.GoToWithoutTokenAsync();

        await _page.WaitForURLAsync(new Regex("/reset-password-complete"), new() { Timeout = 20_000 });

        Assert.True(await page.IsInvalidLinkMessageVisibleAsync());

        await page.ClickBackToForgotPasswordAsync();
        await _page.WaitForURLAsync(new Regex("/forgot-password"), new() { Timeout = 20_000 });
    }
}
