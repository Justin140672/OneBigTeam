using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the marketing site's redesigned signup page (/signup — SignUp.razor). Lives on
/// the "marketing" Aspire resource, so callers should pass the marketing base URL. The page is a
/// static (non-interactive) form posting to /signup-submit, so this drives it with plain
/// Playwright fill/click calls rather than waiting on a Blazor circuit.
/// </summary>
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

    // Scoped to <main> — SiteFooter.razor's Legal section also links "Terms of Service"/"Privacy
    // Policy" (plus Cookie Policy, Acceptable Use Policy, etc.), so an unscoped page-wide locator
    // resolves to two elements (the signup form's own copy and the footer's) and throws a
    // strict-mode violation.
    public ILocator TermsOfServiceLink => page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Link, new() { Name = "Terms of Service" });

    public ILocator PrivacyPolicyLink => page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Link, new() { Name = "Privacy Policy" });

    public ILocator CompanyDetailsLegend => page.Locator("fieldset.signup-fieldset legend", new() { HasText = "Company details" });

    public ILocator AdminAccountLegend => page.Locator("fieldset.signup-fieldset legend", new() { HasText = "Your admin account" });

    public ILocator LogInInsteadLink => page.GetByRole(AriaRole.Link, new() { Name = "Log in instead" });

    public ILocator ResetPasswordLink => page.GetByRole(AriaRole.Link, new() { Name = "reset your password" });

    public Task<bool> IsExistingAccountMessageVisibleAsync() =>
        page.Locator(".form-status-error").IsVisibleAsync();
}
