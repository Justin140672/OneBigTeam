using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the signup journey up to the "check your email" step: submitting the marketing site's
/// "Start free trial" form (/signup on the "marketing" Aspire resource) creates a brand-new
/// company + pending admin user via HR.Api's POST /api/signup, and /signup-submit redirects to
/// /check-your-email?email={email} instead of auto-logging the admin in.
///
/// This file was renamed from SignupToOnboardingJourneyTests and no longer walks the
/// /getting-started task list after signup. /api/signup now creates a real, pending Supabase
/// Auth user requiring email verification (no session/token is issued at signup time), so this
/// file alone still can't reach /getting-started via a genuine verification click.
///
/// Phase D (HR.Web's GET /verify-email callback, /verify-email-error, and the dev-only
/// POST /api/dev/activate-company bypass) IS implemented now — see
/// VerifyEmailJourneyTests for coverage of the verification-error page, the resend-to-marketing
/// bridge, and the dev-activate-company unblock. What's still not testable end-to-end in this
/// environment is a *successful* /verify-email?code=... click: that requires a genuine Supabase
/// verification code, and there is no live Supabase project configured here, so
/// ISupabaseAuthGateway's real HTTP call to Supabase will always fail against blank/placeholder
/// config. Faking/bypassing that gateway for E2E purposes is deliberately out of scope (E2E tests
/// exercise the real, composed Aspire app).
///
/// SignUp.razor is a static (non-interactive) page with a plain HTML form posting to
/// /signup-submit, so this test drives it with a normal Playwright form fill + submit
/// (no Blazor circuit/interactivity wait needed). CheckYourEmail.razor and the
/// /resend-verification proxy are likewise static forms.
/// </summary>
[Collection("E2E")]
public sealed class SignupToCheckYourEmailJourneyTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private async Task<string> SignUpAsync()
    {
        var companyName = $"E2E Signup Co {Guid.NewGuid():N}";
        var email = $"e2e-signup-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123";

        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/signup");
        await _page.FillAsync("#companyName", companyName);
        await _page.FillAsync("#firstName", "Ada");
        await _page.FillAsync("#lastName", "Lovelace");
        await _page.FillAsync("#email", email);
        await _page.FillAsync("#password", password);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Start free trial" }).ClickAsync();

        await _page.WaitForURLAsync(new Regex("/check-your-email"), new() { Timeout = 20_000 });

        return email;
    }

    [Fact]
    public async Task SignUp_RedirectsToCheckYourEmail_WithSubmittedEmailDisplayed()
    {
        var email = await SignUpAsync();

        Assert.Contains("/check-your-email", _page.Url);
        Assert.Contains(Uri.EscapeDataString(email), _page.Url);

        var checkYourEmail = new CheckYourEmailPage(_page, _fixture.MarketingBaseUrl);
        await checkYourEmail.WaitForLoadAsync();

        Assert.Equal(email, await checkYourEmail.GetDisplayedEmailAsync());
    }

    [Fact]
    public async Task ResendVerificationEmail_ShowsResentConfirmation()
    {
        var email = await SignUpAsync();

        var checkYourEmail = new CheckYourEmailPage(_page, _fixture.MarketingBaseUrl);
        await checkYourEmail.WaitForLoadAsync();

        Assert.False(await checkYourEmail.IsResentConfirmationVisibleAsync());

        await checkYourEmail.ClickResendVerificationEmailAsync();

        Assert.Contains("resent=true", _page.Url);
        Assert.True(await checkYourEmail.IsResentConfirmationVisibleAsync());
        Assert.Equal(email, await checkYourEmail.GetDisplayedEmailAsync());
    }

    [Fact]
    public async Task ChangeEmailAddress_NavigatesBackToSignup()
    {
        await SignUpAsync();

        var checkYourEmail = new CheckYourEmailPage(_page, _fixture.MarketingBaseUrl);
        await checkYourEmail.WaitForLoadAsync();

        await checkYourEmail.ClickChangeEmailAddressAsync();

        await _page.WaitForURLAsync(new Regex("/signup$"), new() { Timeout = 20_000 });
        Assert.EndsWith("/signup", _page.Url);
    }
}
