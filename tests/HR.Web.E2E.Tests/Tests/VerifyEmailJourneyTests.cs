using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers Phase D's "verify email" callback (GET /verify-email in HR.Web's Program.cs) as far as
/// this environment allows. There is no live Supabase project configured here, so a *successful*
/// verification click (a real ?code=... that HR.Api's POST /api/verify-email accepts) cannot be
/// driven end-to-end — ISupabaseAuthGateway makes a genuine HTTP call to Supabase that will always
/// fail against blank/placeholder config, and faking that gateway would defeat the point of an E2E
/// test against the real, composed Aspire app. So this file instead covers everything that IS
/// reachable without a live Supabase project:
///   1. The failure path — missing/garbage code redirects to /verify-email-error, which renders.
///   2. That page's resend flow, which bridges over to the marketing site's existing
///      /check-your-email page rather than duplicating its resend UI.
///   3. The dev-only POST /api/dev/activate-company bypass, which exists specifically so
///      local/E2E testing can get a company into the Active state without a live Supabase
///      verification click.
/// </summary>
[Collection("E2E")]
public sealed class VerifyEmailJourneyTests(AppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task VerifyEmail_WithMissingCode_RedirectsToVerifyEmailError()
    {
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/verify-email");

        await _page.WaitForURLAsync(new Regex("/verify-email-error"), new() { Timeout = 20_000 });

        var errorPage = new VerifyEmailErrorPage(_page);
        await errorPage.WaitForLoadAsync();

        Assert.True(await errorPage.IsInvalidLinkMessageVisibleAsync());
    }

    [Fact]
    public async Task VerifyEmail_WithGarbageCode_RedirectsToVerifyEmailError()
    {
        // No live Supabase project is configured in this environment, so any code — genuine
        // format or not — is rejected by HR.Api's POST /api/verify-email.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/verify-email?code=not-a-real-code");

        await _page.WaitForURLAsync(new Regex("/verify-email-error"), new() { Timeout = 20_000 });

        var errorPage = new VerifyEmailErrorPage(_page);
        await errorPage.WaitForLoadAsync();

        Assert.True(await errorPage.IsInvalidLinkMessageVisibleAsync());
    }

    [Fact]
    public async Task VerifyEmailError_ResendVerificationEmail_BridgesToMarketingCheckYourEmail()
    {
        var email = $"e2e-verify-error-{Guid.NewGuid():N}@example.com";

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/verify-email-error");

        var errorPage = new VerifyEmailErrorPage(_page);
        await errorPage.WaitForLoadAsync();

        await errorPage.ResendVerificationEmailAsync(email);

        // SubmitAsync navigates (forceLoad: true) to the marketing site's /check-your-email page.
        await _page.WaitForURLAsync(new Regex("/check-your-email"), new() { Timeout = 20_000 });
        Assert.Contains(_fixture.MarketingBaseUrl, _page.Url);
        Assert.Contains(Uri.EscapeDataString(email), _page.Url);

        var checkYourEmail = new CheckYourEmailPage(_page, _fixture.MarketingBaseUrl);
        await checkYourEmail.WaitForLoadAsync();

        Assert.Equal(email, await checkYourEmail.GetDisplayedEmailAsync());
    }

    /// <summary>
    /// Signs a company up directly against HR.Api's POST /api/signup (rather than driving the
    /// marketing UI, which only echoes the email back — not the company id this test needs) to
    /// get a real PendingVerification company, then exercises the dev-only
    /// POST /api/dev/activate-company bypass that lets local/E2E testing reach the "company is
    /// Active" state without a live Supabase verification click. A full authenticated walk to
    /// /getting-started isn't attempted here: the newly-signed-up admin is a real (pending)
    /// Supabase user, not one of the seeded dev personas the topbar's dev-persona switcher knows
    /// about, so there's no way to log in as them without a real Supabase session — which is
    /// exactly what this environment can't produce. Asserting the endpoint's success response is
    /// the meaningful, environment-appropriate check for "the dev-only unblock mechanism works".
    /// </summary>
    [Fact]
    public async Task DevActivateCompany_ActivatesNewlySignedUpCompany()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var companyName = $"E2E Activate Co {Guid.NewGuid():N}";
        var email = $"e2e-activate-{Guid.NewGuid():N}@example.com";

        var signUpResponse = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = companyName,
            AdminFirstName = "Ada",
            AdminLastName = "Lovelace",
            AdminEmail = email,
            Password = "P@ssw0rd123",
        });

        Assert.True(signUpResponse.IsSuccessStatusCode);

        var signUp = await signUpResponse.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);
        Assert.NotEqual(Guid.Empty, signUp!.CompanyId);

        var activateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company",
            new { CompanyId = signUp.CompanyId });

        Assert.Equal(System.Net.HttpStatusCode.NoContent, activateResponse.StatusCode);

        // Idempotent — a second call against the now-Active company should still succeed rather
        // than error (mirrors ICompanyProvisioner.ActivateCompanyAsync's documented no-op-on-repeat
        // behaviour).
        var repeatActivateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company",
            new { CompanyId = signUp.CompanyId });

        Assert.Equal(System.Net.HttpStatusCode.NoContent, repeatActivateResponse.StatusCode);
    }

    private sealed record SignUpResult(
        Guid UserId,
        Guid CompanyId,
        string Email,
        string FirstName,
        string LastName);
}
