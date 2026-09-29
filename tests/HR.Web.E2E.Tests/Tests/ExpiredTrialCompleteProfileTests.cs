using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ExpiredTrialCompleteProfileTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    private const string DialogSeenFlag = "__completionDialogSeen";

    private async Task<string> SignUpActivateAndExpireFreshCompanyAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"e2e-expired-trial-{suffix}@example.com";

        var signUpResponse = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = $"E2E Expired Trial Co {suffix}",
            AdminFirstName = "Placeholder",
            AdminLastName = "Admin",
            AdminEmail = email,
            Password = DevPersonaPassword,
        });
        Assert.True(signUpResponse.IsSuccessStatusCode);

        var signUp = await signUpResponse.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);

        var activateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company", new { CompanyId = signUp!.CompanyId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, activateResponse.StatusCode);

        var expireResponse = await http.PostAsJsonAsync(
            "/api/dev/expire-company-trial", new { CompanyId = signUp.CompanyId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, expireResponse.StatusCode);

        return email;
    }

    private async Task LoginWithDialogWatcherAsync(string email)
    {
        await _page.AddInitScriptAsync($$"""
            window.{{DialogSeenFlag}} = false;
            new MutationObserver(() => {
                if (document.querySelector('.employee-completion-dialog')) window.{{DialogSeenFlag}} = true;
            }).observe(document, { childList: true, subtree: true });
            """);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email, DevPersonaPassword);
        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExpiredTrialAdmin_WithIncompleteProfile_NeverSeesCompleteProfileDialog()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);

        Assert.False(await _page.Locator(".employee-completion-dialog").IsVisibleAsync());
        Assert.False(await _page.EvaluateAsync<bool>($"window.{DialogSeenFlag} === true"),
            "The Complete Profile dialog must never render for a read-only company, not even briefly.");
    }

    [Fact]
    public async Task ExpiredTrialAdmin_SeesReadOnlyBanner_AndCanOpenSubscriptionAndStartCheckout()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);

        var banner = _page.Locator(".trial-banner--expired");
        await banner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.Contains("read-only", await banner.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await banner.Locator(".trial-banner-action").ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/subscription"), new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        var billing = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);
        Assert.True(await billing.HasStartSubscriptionButtonAsync());
        Assert.False(await _page.Locator(".employee-completion-dialog").IsVisibleAsync());
    }

    private sealed record SignUpResult(Guid UserId, Guid CompanyId);
}
