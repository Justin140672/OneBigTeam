using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 9: account creation with a public/disposable email domain is blocked server-side.
/// Covers the marketing /signup page (SignUp.razor + /signup-submit in HR.Marketing/Program.cs):
/// the organisation-email hint shown before submission, the redirect back to /signup with
/// emailError=work_email_required when HR.Api's AccountCreationEmailGuard rejects the domain
/// (banner + accessible field error + round-tripped fields, password never round-tripped), and
/// that an organisation domain still proceeds to /check-your-email.
///
/// SignUp.razor is static SSR (no Blazor circuit), so aria-invalid is rendered as a literal
/// "true"/"false" string — asserting ToHaveAttributeAsync("aria-invalid", "true") is safe here.
/// </summary>
public sealed class SignupWorkEmailPolicyTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string WorkEmailRequiredMessage =
        "Please use your organisation's work email address. Public email services such as Gmail, Hotmail and Outlook.com cannot be used to create an account.";

    private const string Password = "P@ssw0rd123";

    [Fact]
    public async Task SignupForm_ShowsOrganisationEmailHint_BeforeSubmission()
    {
        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();

        await Assertions.Expect(signUp.EmailHint).ToBeVisibleAsync();
        await Assertions.Expect(signUp.EmailHint).ToContainTextAsync("Use your organisation's email address.");
        await Assertions.Expect(signUp.EmailHint).ToContainTextAsync(
            "Personal email services such as Gmail, Hotmail and Outlook.com can't be used to create an account.");

        await Assertions.Expect(signUp.EmailInput).ToHaveAttributeAsync(
            "aria-describedby", new Regex(@"(^|\s)email-hint(\s|$)"));

        // Nothing has been rejected yet — the field must not start out in the invalid state.
        await Assertions.Expect(signUp.EmailInput).ToHaveAttributeAsync("aria-invalid", "false");
        await Assertions.Expect(signUp.EmailFieldWrapper).Not.ToHaveClassAsync(new Regex(@"(^|\s)is-invalid(\s|$)"));
        await Assertions.Expect(signUp.WorkEmailError).ToHaveCountAsync(0);
        await Assertions.Expect(signUp.ErrorBanner).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("hotmail.com")]
    [InlineData("outlook.com")]
    [InlineData("GMAIL.COM")]
    public async Task SignUp_WithPublicEmailDomain_StaysOnSignup_WithAccessibleWorkEmailError(string domain)
    {
        var unique = Guid.NewGuid().ToString("N");
        var companyName = $"E2E Work Email Co {unique}";
        const string firstName = "Ada";
        const string lastName = "Lovelace";
        var email = $"e2e-work-email-{unique}@{domain}";

        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();
        await signUp.FillAsync(companyName, firstName, lastName, email, Password);
        await signUp.SubmitAsync();

        // /signup-submit proxies to HR.Api's POST /api/signup; the email-domain guard rejects
        // before any Supabase call, but keep the same headroom as the other signup redirects.
        await _page.WaitForURLAsync(new Regex(@"/signup\?.*emailError=work_email_required"), new() { Timeout = 40_000 });
        Assert.DoesNotContain("/check-your-email", _page.Url);
        Assert.DoesNotContain("password", _page.Url, StringComparison.OrdinalIgnoreCase);

        // Page-level banner.
        await Assertions.Expect(signUp.ErrorBanner).ToBeVisibleAsync();
        await Assertions.Expect(signUp.ErrorBanner).ToContainTextAsync(WorkEmailRequiredMessage);

        // Field-level error, linked to the input and flagged invalid.
        await Assertions.Expect(signUp.WorkEmailError).ToBeVisibleAsync();
        await Assertions.Expect(signUp.WorkEmailError).ToHaveTextAsync(WorkEmailRequiredMessage);
        await Assertions.Expect(signUp.EmailInput).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(signUp.EmailInput).ToHaveAttributeAsync(
            "aria-describedby", new Regex(@"(^|\s)email-error(\s|$)"));
        await Assertions.Expect(signUp.EmailFieldWrapper).ToHaveClassAsync(new Regex(@"(^|\s)is-invalid(\s|$)"));

        // Everything except the password is round-tripped.
        await Assertions.Expect(_page.Locator("#companyName")).ToHaveValueAsync(companyName);
        await Assertions.Expect(_page.Locator("#firstName")).ToHaveValueAsync(firstName);
        await Assertions.Expect(_page.Locator("#lastName")).ToHaveValueAsync(lastName);
        await Assertions.Expect(signUp.EmailInput).ToHaveValueAsync(email);
        await Assertions.Expect(signUp.PasswordInput).ToHaveValueAsync("");
    }

    [Fact]
    public async Task SignUp_WithOrganisationEmailDomain_RedirectsToCheckYourEmail()
    {
        var unique = Guid.NewGuid().ToString("N");
        var companyName = $"E2E Work Email Org Co {unique}";
        var email = $"e2e-work-email-{unique}@brightsparks-consulting.co.uk";

        var signUp = new SignUpPage(_page, _fixture.MarketingBaseUrl);
        await signUp.GoToAsync();
        await signUp.FillAsync(companyName, "Ada", "Lovelace", email, Password);
        await signUp.SubmitAsync();

        // Same real-Supabase-call headroom as SignupToCheckYourEmailJourneyTests.SignUpAsync.
        await _page.WaitForURLAsync(new Regex("/check-your-email"), new() { Timeout = 40_000 });

        Assert.Contains(Uri.EscapeDataString(email), _page.Url);
        Assert.DoesNotContain("emailError", _page.Url);
    }
}
