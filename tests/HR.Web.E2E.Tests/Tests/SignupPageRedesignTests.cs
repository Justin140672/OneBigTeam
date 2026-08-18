using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the redesigned /signup page (SignUp.razor + the new SignupHeader/SignupFooter
/// components): the simplified header's "Log in" link (no more dead "Start free trial" link on
/// this page), the fieldset/legend grouping, the real Terms of Service / Privacy Policy links,
/// the existing-account (409 Conflict) redirect path from /signup-submit, and field round-
/// tripping on a correctable error.
///
/// Like SignupToCheckYourEmailJourneyTests, this drives a plain HTML form (no Blazor circuit).
/// </summary>
public sealed class SignupPageRedesignTests(ParallelBlankPersonaFixture fixture)
    : SupabaseAuthSerialBlankTestBase(fixture)
{
    [Fact]
    public async Task SignupHeader_LoginLink_PointsAtWebLogin_NotHash()
    {
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();

        var href = await signUp.LoginLink.GetAttributeAsync("href");

        Assert.NotNull(href);
        Assert.NotEqual("#", href);
        Assert.Contains(_fixture.WebBaseUrl, href);
        Assert.EndsWith("/login", href);
    }

    [Fact]
    public async Task SignupHeader_LoginLink_NavigatesToWebLoginPage()
    {
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();

        await signUp.LoginLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/login"), new() { Timeout = 20_000 });
        Assert.Contains(_fixture.WebBaseUrl, _page.Url);
        Assert.EndsWith("/login", _page.Url);
    }

    [Fact]
    public async Task SignupForm_HasFieldsetLegendGrouping()
    {
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();

        await Assertions.Expect(signUp.CompanyDetailsLegend).ToBeVisibleAsync();
        await Assertions.Expect(signUp.AdminAccountLegend).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SignupForm_TermsAndPrivacyLinks_AreRealAnchorsWithCorrectHrefs()
    {
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();

        await Assertions.Expect(signUp.TermsOfServiceLink).ToBeVisibleAsync();
        await Assertions.Expect(signUp.PrivacyPolicyLink).ToBeVisibleAsync();

        Assert.Equal("/terms-of-service", await signUp.TermsOfServiceLink.GetAttributeAsync("href"));
        Assert.Equal("/privacy-policy", await signUp.PrivacyPolicyLink.GetAttributeAsync("href"));
    }

    private async Task<(string CompanyName, string FirstName, string LastName, string Email)> SignUpAsync()
    {
        var companyName = $"E2E Redesign Co {Guid.NewGuid():N}";
        var firstName = "Ada";
        var lastName = "Lovelace";
        var email = $"e2e-redesign-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123";

        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();
        await signUp.FillAsync(companyName, firstName, lastName, email, password);
        await signUp.SubmitAsync();

        // /signup-submit now makes a real Supabase Auth account-creation call (see the
        // "fixing supabase auth" work) before it can redirect — that's a genuine external network
        // round trip, not a local Blazor/grid render, so it needs materially more headroom than
        // the 20s that was enough for the old dev-stub signup. 40s matches the margin used
        // elsewhere in this suite for other real-network-call waits.
        await _page.WaitForURLAsync(new Regex("/check-your-email"), new() { Timeout = 40_000 });

        return (companyName, firstName, lastName, email);
    }

    [Fact]
    public async Task SignUp_WithAlreadyRegisteredEmail_RedirectsToSignupWithExistingAccountMessage()
    {
        var (companyName, firstName, lastName, email) = await SignUpAsync();

        // Attempt to sign up again with the same email — HR.Api's /api/signup returns 409
        // Conflict, and /signup-submit redirects back to /signup with existingEmail=true plus
        // the round-tripped field values.
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();
        await signUp.FillAsync(companyName, firstName, lastName, email, "AnotherP@ss123");
        await signUp.SubmitAsync();

        // Same real-Supabase-call reasoning as SignUpAsync's own wait above — this second submit
        // also round-trips to Supabase (to discover the email already exists) before /signup-submit
        // can redirect back.
        await _page.WaitForURLAsync(new Regex("/signup\\?"), new() { Timeout = 40_000 });
        Assert.Contains("existingEmail=true", _page.Url);

        Assert.True(await signUp.IsExistingAccountMessageVisibleAsync());
        await Assertions.Expect(signUp.LogInInsteadLink).ToBeVisibleAsync();
        await Assertions.Expect(signUp.ResetPasswordLink).ToBeVisibleAsync();

        var loginHref = await signUp.LogInInsteadLink.GetAttributeAsync("href");
        Assert.NotNull(loginHref);
        Assert.EndsWith("/login", loginHref);

        var resetHref = await signUp.ResetPasswordLink.GetAttributeAsync("href");
        Assert.NotNull(resetHref);
        Assert.EndsWith("/forgot-password", resetHref);
    }

    [Fact]
    public async Task SignUp_WithAlreadyRegisteredEmail_RetainsFieldValuesExceptPassword()
    {
        var (companyName, firstName, lastName, email) = await SignUpAsync();

        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();
        await signUp.FillAsync(companyName, firstName, lastName, email, "AnotherP@ss123");
        await signUp.SubmitAsync();

        // Same real-Supabase-call reasoning as above.
        await _page.WaitForURLAsync(new Regex("/signup\\?"), new() { Timeout = 40_000 });

        Assert.Equal(companyName, await signUp.GetCompanyNameValueAsync());
        Assert.Equal(firstName, await signUp.GetFirstNameValueAsync());
        Assert.Equal(lastName, await signUp.GetLastNameValueAsync());
        Assert.Equal(email, await signUp.GetEmailValueAsync());

        // Password is deliberately never round-tripped.
        var passwordValue = await _page.Locator("#password").InputValueAsync();
        Assert.Equal("", passwordValue);
    }
}
