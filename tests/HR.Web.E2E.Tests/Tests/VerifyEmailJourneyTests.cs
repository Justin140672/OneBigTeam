using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VerifyEmailJourneyTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
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

        await _page.WaitForURLAsync(new Regex("/check-your-email"), new() { Timeout = 20_000 });
        Assert.Contains(_fixture.MarketingBaseUrl, _page.Url);
        Assert.Contains(Uri.EscapeDataString(email), _page.Url);

        var checkYourEmail = new CheckYourEmailPage(_page, _fixture.MarketingBaseUrl);
        await checkYourEmail.WaitForLoadAsync();

        Assert.Equal(email, await checkYourEmail.GetDisplayedEmailAsync());
    }

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
