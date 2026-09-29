using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SignUpPage(IPage page, string marketingBaseUrl)
{
    public Task GoToAsync() => page.GotoAsync($"{marketingBaseUrl}/signup");

    public Task FillAsync(string companyName, string firstName, string lastName, string email, string password)
    {
        return FillFormAsync(companyName, firstName, lastName, email, password);
    }

    private async Task FillFormAsync(string companyName, string firstName, string lastName, string email, string password)
    {
        await page.FillAsync("#companyName", companyName);
        await page.FillAsync("#firstName", firstName);
        await page.FillAsync("#lastName", lastName);
        await page.FillAsync("#email", email);
        if (!string.IsNullOrEmpty(password))
        {
            await page.FillAsync("#password", password);
        }
    }

    public Task SubmitAsync() =>
        page.Locator("[data-signup-submit]").ClickAsync();

    public Task<string?> GetCompanyNameValueAsync() => page.Locator("#companyName").InputValueAsync()!;
    public Task<string?> GetFirstNameValueAsync() => page.Locator("#firstName").InputValueAsync()!;
    public Task<string?> GetLastNameValueAsync() => page.Locator("#lastName").InputValueAsync()!;
    public Task<string?> GetEmailValueAsync() => page.Locator("#email").InputValueAsync()!;

    public ILocator LoginLink => page.GetByRole(AriaRole.Link, new() { Name = "Log in" });

    public ILocator TermsOfServiceLink => page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Link, new() { Name = "Terms of Service" });

    public ILocator PrivacyPolicyLink => page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Link, new() { Name = "Privacy Policy" });

    public ILocator CompanyDetailsLegend => page.Locator("fieldset.signup-fieldset legend", new() { HasText = "Company details" });

    public ILocator AdminAccountLegend => page.Locator("fieldset.signup-fieldset legend", new() { HasText = "Your admin account" });

    public ILocator LogInInsteadLink => page.GetByRole(AriaRole.Link, new() { Name = "Log in instead" });

    public ILocator ResetPasswordLink => page.GetByRole(AriaRole.Link, new() { Name = "reset your password" });

    public Task<bool> IsExistingAccountMessageVisibleAsync() =>
        page.Locator(".form-status-error").IsVisibleAsync();

    // ── Ticket 9: organisation (work) email requirement ─────────────────────────────────────────

    public ILocator ErrorBanner => page.Locator(".form-status-error[role='alert'][data-status='error']");

    public ILocator PasswordInput => page.Locator("#password");

    public ILocator EmailInput => page.Locator("#email");

    public ILocator EmailHint => page.Locator("#email-hint[data-email-hint]");

    public ILocator EmailFieldWrapper => page.Locator(".form-field[data-email-field]");

    /// <summary>The email field error rendered only when the server rejected the email domain.</summary>
    public ILocator WorkEmailError => page.Locator("#email-error[data-work-email-error]");
}
